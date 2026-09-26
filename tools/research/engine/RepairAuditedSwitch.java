//@category Accessibility
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.listing.Function;
import ghidra.program.model.pcode.JumpTable;
import ghidra.program.model.symbol.*;
import java.util.*;
import java.nio.file.*;

/** Annotate the two jump-table sites requiring overrides in 16E7A0.
 * The native instructions and table bytes are not changed. */
public class RepairAuditedSwitch extends GhidraScript {
    protected void run() throws Exception {
        if (!"8FE9D75E4CDC279645C5BC932FC163FD67147255FC0C673AC45BBF0A6D2E00D7".equalsIgnoreCase(currentProgram.getExecutableSHA256())) throw new IllegalStateException("Hash mismatch");
        Address base = currentProgram.getImageBase();
        long[] table = {0x16E817,0x16E8D7,0x16E8E5,0x16E85B,0x16E87F,0x16E8B0,0x16E8C1};
        for (int i=0;i<table.length;i++) if (Integer.toUnsignedLong(getInt(base.add(0x16E904+i*4))) != base.getOffset()+table[i]) throw new IllegalStateException("Native table changed");
        Function f = getFunctionAt(base.add(0x16E7A0));
        // Retain the original analyzed entry body. The decompiler follows the
        // proven jump targets into their existing instruction blocks itself.
        f.setBody(new AddressSet(base.add(0x16E7A0),base.add(0x16E816)));
        long oldSize = f.getBody().getNumAddresses();
        long[] sites = {0x16E7D8,0x16E810};
        int[][] indices = {{3},{0,4,6}};
        StringBuilder log = new StringBuilder("site_rva\tnative_indices\ttarget_rvas\n");
        for (int i=0;i<sites.length;i++) {
            Address site = base.add(sites[i]);
            ArrayList<Address> targets = new ArrayList<>();
            for (int index:indices[i]) {
                Address target = base.add(table[index]); targets.add(target);
                disassemble(target);
                currentProgram.getReferenceManager().addMemoryReference(site, target, RefType.COMPUTED_JUMP, SourceType.USER_DEFINED, 0);
            }
            new JumpTable(site,targets,true,0).writeOverride(f);
            log.append(String.format("%08X\t%s\t%s%n",sites[i],Arrays.toString(indices[i]),targets));
        }
        log.append("Body bytes: ").append(oldSize).append(" -> ").append(f.getBody().getNumAddresses()).append("\n");
        Files.writeString(Paths.get(getScriptArgs()[0]).resolve("audited-switch-repair.tsv"),log.toString());
        println(log.toString());
    }
}
