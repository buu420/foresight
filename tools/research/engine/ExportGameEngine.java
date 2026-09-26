//@category Accessibility
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Complete, read-only export of the supported program's analyzed code and data index. */
public class ExportGameEngine extends GhidraScript {
    private static final String SHA = "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7";
    private long base;
    private String rva(Address a) { return String.format("%08X", a.getOffset() - base); }
    private String clean(Object value) { return String.valueOf(value).replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n"); }
    private PrintWriter writer(Path path) throws Exception { return new PrintWriter(Files.newBufferedWriter(path, StandardCharsets.UTF_8)); }
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) throw new IllegalArgumentException("Output directory required");
        String expected = args.length > 1 ? args[1] : SHA;
        if (!expected.matches("[a-fA-F0-9]{64}") || !expected.equalsIgnoreCase(currentProgram.getExecutableSHA256())) throw new IllegalStateException("Program identity mismatch");
        Path out = Paths.get(args[0]).toAbsolutePath().normalize();
        Files.createDirectories(out.resolve("functions"));
        base = currentProgram.getImageBase().getOffset();
        Listing listing = currentProgram.getListing();
        AddressSet functionBodies = new AddressSet();
        long executableBytes = 0, instructionBytes = 0, instructionCount = 0;
        int functionCount = 0, complete = 0, failed = 0, external = 0;
        try (PrintWriter blocks = writer(out.resolve("memory-blocks.tsv"))) {
            blocks.println("name\tstart_rva\tend_rva\tbytes\tread\twrite\texecute\tinitialized");
            for (MemoryBlock block : currentProgram.getMemory().getBlocks()) {
                blocks.printf("%s\t%s\t%s\t%d\t%b\t%b\t%b\t%b%n", clean(block.getName()), rva(block.getStart()), rva(block.getEnd()), block.getSize(), block.isRead(), block.isWrite(), block.isExecute(), block.isInitialized());
                if (block.isExecute()) executableBytes += block.getSize();
            }
        }
        try (PrintWriter symbols = writer(out.resolve("symbols.tsv"))) {
            symbols.println("address_rva\tname\ttype\tsource\texternal");
            SymbolIterator it = currentProgram.getSymbolTable().getAllSymbols(true);
            while (it.hasNext()) {
                Symbol s = it.next();
                symbols.printf("%s\t%s\t%s\t%s\t%b%n", rva(s.getAddress()), clean(s.getName(true)), s.getSymbolType(), s.getSource(), s.isExternal());
            }
        }
        try (PrintWriter strings = writer(out.resolve("defined-data.tsv"))) {
            strings.println("address_rva\tbytes\ttype\tvalue");
            DataIterator data = listing.getDefinedData(true);
            while (data.hasNext()) {
                Data d = data.next();
                strings.printf("%s\t%d\t%s\t%s%n", rva(d.getAddress()), d.getLength(), clean(d.getDataType().getPathName()), clean(d.getDefaultValueRepresentation()));
            }
        }
        DecompInterface di = new DecompInterface();
        di.setOptions(new DecompileOptions());
        if (!di.openProgram(currentProgram)) throw new IllegalStateException(di.getLastMessage());
        try (PrintWriter index = writer(out.resolve("functions.tsv"));
             PrintWriter calls = writer(out.resolve("calls.tsv"));
             PrintWriter errors = writer(out.resolve("decompile-failures.tsv"));
             PrintWriter combined = writer(out.resolve("game.c"))) {
            index.println("rva\tname\tbytes\tthunk\texternal\tstatus\tfile\tprototype");
            calls.println("from_rva\tto_rva\tto_name");
            errors.println("rva\tname\terror");
            combined.println("/* Ghidra decompiler output; not original source. SHA256 " + expected + " */");
            FunctionIterator functions = currentProgram.getFunctionManager().getFunctions(true);
            while (functions.hasNext() && !monitor.isCancelled()) {
                Function f = functions.next();
                String id = rva(f.getEntryPoint()), file = "functions/" + id + ".c";
                functionCount++;
                functionBodies.add(f.getBody());
                if (f.isExternal()) { external++; continue; }
                for (Function callee : f.getCalledFunctions(monitor)) calls.printf("%s\t%s\t%s%n", id, rva(callee.getEntryPoint()), clean(callee.getName(true)));
                DecompileResults result = di.decompileFunction(f, 60, monitor);
                boolean ok = result != null && result.decompileCompleted() && result.getDecompiledFunction() != null;
                String code;
                if (ok) { complete++; code = result.getDecompiledFunction().getC(); }
                else {
                    failed++;
                    String error = result == null ? "No result" : result.getErrorMessage();
                    errors.printf("%s\t%s\t%s%n", id, clean(f.getName(true)), clean(error));
                    code = "/* DECOMPILATION FAILED: " + clean(error).replace("*/", "* /") + " */\n";
                }
                String header = "/* RVA 0x" + id + "; " + f.getName(true) + "; " + f.getBody().getNumAddresses() + " bytes */\n";
                Files.writeString(out.resolve(file), header + code, StandardCharsets.UTF_8);
                combined.println(header + code);
                index.printf("%s\t%s\t%d\t%b\t%b\t%s\t%s\t%s%n", id, clean(f.getName(true)), f.getBody().getNumAddresses(), f.isThunk(), f.isExternal(), ok ? "decompiled" : "failed", file, clean(f.getPrototypeString(true, true)));
                if (functionCount % 250 == 0) {
                    index.flush(); calls.flush(); errors.flush(); combined.flush();
                    String progress = "functions=" + functionCount + " decompiled=" + complete + " failures=" + failed;
                    Files.writeString(out.resolve("progress.txt"), progress + "\n", StandardCharsets.UTF_8);
                    println(progress);
                }
            }
        } finally { di.dispose(); }
        if (monitor.isCancelled()) throw new IOException("Export cancelled; partial output is not complete");
        try (PrintWriter asm = writer(out.resolve("game.asm")); PrintWriter uncovered = writer(out.resolve("unassigned-instructions.tsv"))) {
            asm.println("; All Ghidra-defined instructions, including those outside functions. SHA256 " + expected);
            uncovered.println("rva\tbytes\tdisassembly");
            InstructionIterator instructions = listing.getInstructions(true);
            while (instructions.hasNext()) {
                Instruction i = instructions.next();
                StringBuilder hex = new StringBuilder();
                for (byte b : i.getBytes()) hex.append(String.format("%02X", b & 255));
                String row = rva(i.getAddress()) + "\t" + hex + "\t" + i.toString();
                asm.println(row);
                if (!functionBodies.contains(i.getAddress())) uncovered.println(row);
                instructionCount++; instructionBytes += i.getLength();
            }
        }
        // Executable sections also contain alignment/data. Preserve all bytes that
        // analysis did not classify, rather than pretending every byte is a function.
        try (PrintWriter gaps = writer(out.resolve("undefined-executable-ranges.tsv"))) {
            gaps.println("start_rva\tend_rva\tbytes");
            for (MemoryBlock b : currentProgram.getMemory().getBlocks()) {
                if (!b.isExecute() || !b.isInitialized()) continue;
                Address start = null, last = null;
                Address p = b.getStart();
                while (p != null && p.compareTo(b.getEnd()) <= 0) {
                    CodeUnit unit = listing.getCodeUnitContaining(p);
                    boolean undefined = unit == null || unit instanceof Data && !((Data)unit).isDefined();
                    if (undefined) { if (start == null) start = p; last = p; p = p.next(); }
                    else {
                        if (start != null) { gaps.printf("%s\t%s\t%d%n", rva(start), rva(last), last.subtract(start) + 1); start = null; }
                        p = unit.getMaxAddress().next();
                    }
                }
                if (start != null) gaps.printf("%s\t%s\t%d%n", rva(start), rva(last), last.subtract(start) + 1);
            }
        }
        String json = String.format(Locale.ROOT,
            "{\n  \"executableSha256\": \"%s\",\n  \"imageBase\": \"%08X\",\n  \"functions\": %d,\n  \"decompiled\": %d,\n  \"failed\": %d,\n  \"external\": %d,\n  \"executableBytes\": %d,\n  \"instructionBytes\": %d,\n  \"instructions\": %d,\n  \"functionBodyBytes\": %d,\n  \"exportComplete\": true\n}\n",
            expected, base, functionCount, complete, failed, external, executableBytes, instructionBytes, instructionCount, functionBodies.getNumAddresses());
        Files.writeString(out.resolve("manifest.json"), json, StandardCharsets.UTF_8);
        Files.writeString(out.resolve("progress.txt"), "Export complete.\n" + json, StandardCharsets.UTF_8);
        println(json);
    }
}
