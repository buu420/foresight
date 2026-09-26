//@category Accessibility
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.block.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Preserve and decompile isolated instruction flows omitted by automatic function discovery. */
public class RecoverFunctionGaps extends GhidraScript {
    private String clean(Object v) { return String.valueOf(v).replace("\t", " ").replace("\n", "\\n").replace("\r", ""); }
    protected void run() throws Exception {
        String expected = getScriptArgs().length > 1 ? getScriptArgs()[1] : "8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7";
        if (!expected.matches("[a-fA-F0-9]{64}") || !expected.equalsIgnoreCase(currentProgram.getExecutableSHA256())) throw new IllegalStateException("Hash mismatch");
        Path out = Paths.get(getScriptArgs()[0]).toAbsolutePath().normalize();
        Files.createDirectories(out.resolve("functions"));
        long base = currentProgram.getImageBase().getOffset();
        Listing listing = currentProgram.getListing();
        AddressSet unowned = new AddressSet();
        InstructionIterator ins = listing.getInstructions(true);
        while (ins.hasNext()) { Instruction i = ins.next(); unowned.add(i.getMinAddress(), i.getMaxAddress()); }
        FunctionIterator existing = listing.getFunctions(true);
        while (existing.hasNext()) unowned.delete(existing.next().getBody());
        TreeSet<Address> candidates = new TreeSet<>();
        CodeBlockIterator blocks = new IsolatedEntrySubModel(currentProgram).getCodeBlocksContaining(unowned, monitor);
        while (blocks.hasNext()) candidates.add(blocks.next().getFirstStartAddress());
        DecompInterface di = new DecompInterface(); di.setOptions(new DecompileOptions()); di.openProgram(currentProgram);
        int made = 0, done = 0, failed = 0;
        try (PrintWriter index = new PrintWriter(Files.newBufferedWriter(out.resolve("functions.tsv"), StandardCharsets.UTF_8));
             PrintWriter evidence = new PrintWriter(Files.newBufferedWriter(out.resolve("candidates.tsv"), StandardCharsets.UTF_8));
             PrintWriter errors = new PrintWriter(Files.newBufferedWriter(out.resolve("decompile-failures.tsv"), StandardCharsets.UTF_8));
             PrintWriter calls = new PrintWriter(Files.newBufferedWriter(out.resolve("calls.tsv"), StandardCharsets.UTF_8))) {
            index.println("rva\tname\tbytes\tthunk\texternal\tstatus\tfile\tprototype");
            evidence.println("rva\torigin\tresult");
            calls.println("from_rva\tto_rva\tto_name");
            errors.println("rva\tname\terror");
            for (Address a : candidates) {
                String id = String.format("%08X", a.getOffset() - base);
                Instruction i = listing.getInstructionAt(a);
                if (i == null || getFunctionContaining(a) != null) continue;
                if (Set.of("NOP", "INT3", "HLT").contains(i.getMnemonicString())) { evidence.println(id + "\tpadding\tskipped"); continue; }
                boolean referenced = false, internalBranch = false;
                for (Reference ref : getReferencesTo(a)) {
                    if (ref.getReferenceType().isCall() || ref.getReferenceType().isData()) referenced = true;
                    else if (ref.getReferenceType().isJump() && getFunctionContaining(ref.getFromAddress()) != null) internalBranch = true;
                }
                if (internalBranch && !referenced) { evidence.println(id + "\tbranch from existing function\tretained in assembly"); continue; }
                String origin = referenced ? "native call or pointer reference" : "isolated instruction-flow candidate";
                Function f;
                try { f = createFunction(a, null); }
                catch (Exception ex) { evidence.println(id + "\t" + origin + "\t" + clean(ex.getMessage())); continue; }
                if (f == null) { evidence.println(id + "\t" + origin + "\tfunction creation declined"); continue; }
                made++;
                di.flushCache();
                DecompileResults result = di.decompileFunction(f, 60, monitor);
                boolean ok = result != null && result.decompileCompleted() && result.getDecompiledFunction() != null;
                String text = "/* Recovered RVA 0x" + id + "; " + origin + "; Ghidra-generated function boundary. */\n";
                if (ok) { done++; text += result.getDecompiledFunction().getC(); }
                else {
                    failed++;
                    String error = clean(result == null ? "null result" : result.getErrorMessage());
                    text += "/* DECOMPILATION FAILED: " + error.replace("*/", "* /") + " */\n";
                    errors.println(id + "\t" + clean(f.getName(true)) + "\t" + error);
                }
                Files.writeString(out.resolve("functions/" + id + ".c"), text, StandardCharsets.UTF_8);
                index.printf("%s\t%s\t%d\t%b\tfalse\t%s\tfunctions/%s.c\t%s%n", id, clean(f.getName(true)), f.getBody().getNumAddresses(), f.isThunk(), ok ? "decompiled" : "failed", id, clean(f.getPrototypeString(true, true)));
                evidence.println(id + "\t" + origin + "\t" + (ok ? "decompiled" : "failed"));
                for (Function callee : f.getCalledFunctions(monitor)) calls.printf("%s\t%08X\t%s%n", id, callee.getEntryPoint().getOffset()-base, clean(callee.getName(true)));
                if (made % 100 == 0) { index.flush(); evidence.flush(); calls.flush(); println("Recovered " + made + " functions"); }
            }
        } finally { di.dispose(); }
        Files.writeString(out.resolve("manifest.json"), String.format(Locale.ROOT,
            "{\"executableSha256\":\"%s\",\"candidates\":%d,\"functions\":%d,\"decompiled\":%d,\"failed\":%d,\"exportComplete\":true}\n", expected, candidates.size(), made, done, failed));
        println("Candidates=" + candidates.size() + " functions=" + made + " decompiled=" + done + " failed=" + failed);
    }
}
