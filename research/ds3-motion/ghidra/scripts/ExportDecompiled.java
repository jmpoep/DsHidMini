// Headless post-script: decompile every function in the program and write the
// output (plus exports, strings and data) to <args[0]>.
// @category DsHidMini
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.DataType;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;
import ghidra.program.model.symbol.SymbolType;
import ghidra.program.model.listing.DataIterator;

import java.io.FileWriter;
import java.io.PrintWriter;

public class ExportDecompiled extends GhidraScript {

    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        String outPath = args.length > 0 ? args[0] : currentProgram.getName() + ".decompiled.c";

        try (PrintWriter out = new PrintWriter(new FileWriter(outPath))) {
            out.println("// Program: " + currentProgram.getName());
            out.println("// Language: " + currentProgram.getLanguageID() + " / " + currentProgram.getCompilerSpec().getCompilerSpecID());
            out.println("// Image base: " + currentProgram.getImageBase());
            out.println();

            out.println("// ===== Exported / external symbols =====");
            SymbolIterator syms = currentProgram.getSymbolTable().getAllSymbols(false);
            while (syms.hasNext()) {
                Symbol s = syms.next();
                if (s.isExternalEntryPoint() || s.getSymbolType() == SymbolType.FUNCTION && s.isGlobal()) {
                    out.println("// " + s.getAddress() + "  " + s.getName(true) + (s.isExternalEntryPoint() ? "  [EXPORT]" : ""));
                }
            }
            out.println();

            out.println("// ===== Defined data =====");
            DataIterator dit = currentProgram.getListing().getDefinedData(true);
            while (dit.hasNext()) {
                Data d = dit.next();
                DataType dt = d.getDataType();
                String name = dt != null ? dt.getName() : "?";
                if (d.getLength() <= 64) {
                    out.println("// " + d.getAddress() + "  " + name + "  " + d.getDefaultValueRepresentation() + (d.getLabel() != null ? "  (" + d.getLabel() + ")" : ""));
                }
            }
            out.println();

            DecompInterface decomp = new DecompInterface();
            decomp.openProgram(currentProgram);

            FunctionIterator fit = currentProgram.getFunctionManager().getFunctions(true);
            while (fit.hasNext()) {
                Function f = fit.next();
                if (f.isThunk() && f.getThunkedFunction(true).isExternal()) {
                    continue;
                }
                out.println("// ===== " + f.getName() + " @ " + f.getEntryPoint() + " (" + f.getCallingConventionName() + ") =====");
                out.println("// callers: " + f.getCallingFunctions(monitor));
                out.println("// callees: " + f.getCalledFunctions(monitor));
                DecompileResults res = decomp.decompileFunction(f, 60, monitor);
                if (res != null && res.decompileCompleted()) {
                    out.println(res.getDecompiledFunction().getC());
                } else {
                    out.println("// decompile failed: " + (res != null ? res.getErrorMessage() : "null"));
                }
                out.println();
            }

            decomp.dispose();
        }

        println("Wrote " + outPath);
    }
}
