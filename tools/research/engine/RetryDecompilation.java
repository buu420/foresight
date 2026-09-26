//@category Accessibility
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** Retry only failed functions; preserves every failed attempt and output style. */
public class RetryDecompilation extends GhidraScript {
    private DecompileOptions options(String profile) {
        DecompileOptions options = new DecompileOptions();
        if (profile.equals("no-wide-arithmetic")) options.setSimplifyDoublePrecision(false);
        if (profile.equals("no-type-splitting")) {
            options.setSplitStructures(false); options.setSplitArrays(false); options.setSplitPointers(false);
        }
        if (profile.equals("no-predication")) options.setPredicate(false);
        if (profile.equals("preserve-unreachable")) options.setEliminateUnreachable(false);
        return options;
    }
    protected void run() throws Exception {
        Path out = Paths.get(getScriptArgs()[0]).toAbsolutePath().normalize();
        String manifest = Files.readString(out.resolve("manifest.json"));
        if (!manifest.toUpperCase().contains(currentProgram.getExecutableSHA256().toUpperCase())) throw new IllegalStateException("Hash mismatch");
        List<String> failures = Files.readAllLines(out.resolve("decompile-failures.tsv"));
        List<String> remaining = new ArrayList<>(); remaining.add(failures.get(0));
        Path attempts = out.resolve("recovery-attempts.tsv");
        List<String> recoveries = Files.exists(attempts) ? new ArrayList<>(Files.readAllLines(attempts)) : new ArrayList<>();
        if (recoveries.isEmpty()) recoveries.add("rva\tstyle\terror");
        Set<String> recovered = new HashSet<>();
        for (int i = 1; i < failures.size(); i++) {
            String[] row = failures.get(i).split("\t", 3);
            Function f = getFunctionAt(currentProgram.getImageBase().add(Long.parseLong(row[0], 16)));
            boolean ok = false;
            for (String profile : new String[]{"decompile", "no-wide-arithmetic", "no-type-splitting", "no-predication", "preserve-unreachable", "normalize", "firstpass"}) {
                String style = profile.equals("normalize") || profile.equals("firstpass") ? profile : "decompile";
                DecompInterface di = new DecompInterface();
                di.setOptions(options(profile));
                di.setSimplificationStyle(style);
                di.toggleSyntaxTree(false);
                di.openProgram(currentProgram);
                println("Retry " + row[0] + " profile=" + profile);
                DecompileResults result = di.decompileFunction(f, 300, monitor);
                ok = result != null && result.decompileCompleted() && result.getDecompiledFunction() != null;
                if (ok) {
                    String text = "/* RVA 0x" + row[0] + "; " + f.getName(true) + "; recovered using Ghidra profile " + profile + "; native bytes unchanged */\n" + result.getDecompiledFunction().getC();
                    Files.writeString(out.resolve("functions/" + row[0] + ".c"), text, StandardCharsets.UTF_8);
                    recovered.add(row[0]);
                    recoveries.add(row[0] + "\t" + profile + "\t");
                } else {
                    recoveries.add(row[0] + "\t" + profile + "\t" + (result == null ? "null result" : result.getErrorMessage()).replace("\n", "\\n").replace("\t", " "));
                }
                di.dispose();
                Files.write(out.resolve("recovery-attempts.tsv"), recoveries, StandardCharsets.UTF_8);
                if (ok) break;
            }
            if (!ok) remaining.add(failures.get(i));
        }
        if (recovered.isEmpty()) { println("No functions recovered; existing export retained."); return; }
        if (!Files.exists(out.resolve("initial-decompile-failures.tsv"))) Files.copy(out.resolve("decompile-failures.tsv"), out.resolve("initial-decompile-failures.tsv"));
        Files.write(out.resolve("decompile-failures.tsv"), remaining, StandardCharsets.UTF_8);
        List<String> index = Files.readAllLines(out.resolve("functions.tsv"));
        long bodyChange = 0;
        for (int i = 1; i < index.size(); i++) {
            String[] row = index.get(i).split("\t", -1);
            if (recovered.contains(row[0])) {
                long size = getFunctionAt(currentProgram.getImageBase().add(Long.parseLong(row[0],16))).getBody().getNumAddresses();
                bodyChange += size - Long.parseLong(row[2]);
                row[2] = Long.toString(size); row[5] = "decompiled"; index.set(i, String.join("\t", row));
            }
        }
        Files.write(out.resolve("functions.tsv"), index, StandardCharsets.UTF_8);
        int total = index.size() - 1;
        int failed = remaining.size() - 1;
        manifest = manifest.replaceAll("\\\"decompiled\\\": \\d+", "\"decompiled\": " + (total - failed));
        manifest = manifest.replaceAll("\\\"failed\\\": \\d+", "\"failed\": " + failed);
        java.util.regex.Matcher bodyMatch = java.util.regex.Pattern.compile("\"functionBodyBytes\": (\\d+)").matcher(manifest);
        if (bodyMatch.find()) manifest = manifest.replace(bodyMatch.group(), "\"functionBodyBytes\": " + (Long.parseLong(bodyMatch.group(1))+bodyChange));
        Files.writeString(out.resolve("manifest.json"), manifest, StandardCharsets.UTF_8);
        Path combinedPending = out.resolve("game.c.pending");
        ExecutorService readers = Executors.newFixedThreadPool(16);
        try (java.io.BufferedWriter combined = Files.newBufferedWriter(combinedPending, StandardCharsets.UTF_8)) {
            combined.write("/* Ghidra decompiler output; see recovery-attempts.tsv for alternate styles. */\n");
            for (int start = 1; start < index.size(); start += 256) {
                List<Future<String>> batch = new ArrayList<>();
                for (int i = start; i < Math.min(start + 256, index.size()); i++) {
                    Path source = out.resolve(index.get(i).split("\t", -1)[6]);
                    batch.add(readers.submit(() -> Files.readString(source)));
                }
                for (Future<String> source : batch) { combined.write(source.get()); combined.write("\n"); }
            }
        } finally { readers.shutdownNow(); }
        Files.move(combinedPending, out.resolve("game.c"), StandardCopyOption.REPLACE_EXISTING);
        Files.writeString(out.resolve("progress.txt"), "Export complete after recovery.\n" + manifest, StandardCharsets.UTF_8);
        println("Recovered " + recovered.size() + ", remaining " + failed);
    }
}
