#!/usr/bin/env python3
"""Compare stock/fork OpenCL validators without disabling any validation pass."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


IDENTITY = "warpclr.opencl-int64-atomic-extensions/0.1"
BASE = "cl_khr_int64_base_atomics"
EXTENDED = "cl_khr_int64_extended_atomics"


def fixture(width, declarations, operation="Load", extra_capability=""):
    wide = "OpCapability Int64\nOpCapability Int64Atomics\n" if width == 64 else ""
    extensions = "".join('OpExtension "' + name + '"\n' for name in declarations)
    instruction = "OpAtomicLoad %integer %pointer %scope %order"
    if operation in ("And", "Or"):
        instruction = "OpAtomic" + operation + " %integer %pointer %scope %order %one"
    return f"""OpCapability Addresses
OpCapability Kernel
{wide}{extra_capability}{extensions}OpMemoryModel Physical64 OpenCL
OpEntryPoint Kernel %main "atomic_probe"
%void = OpTypeVoid
%integer = OpTypeInt {width} 0
%uint = OpTypeInt 32 0
%pointer_type = OpTypePointer CrossWorkgroup %integer
%function = OpTypeFunction %void %pointer_type
%scope = OpConstant %uint 1
%order = OpConstant %uint 16
%one = OpConstant %integer 1
%main = OpFunction %void None %function
%pointer = OpFunctionParameter %pointer_type
%block = OpLabel
%observed = {instruction}
OpReturn
OpFunctionEnd
""".replace("%uint = OpTypeInt 32 0\n", "" if width == 32 else "%uint = OpTypeInt 32 0\n").replace(
        "%uint ", "%integer " if width == 32 else "%uint ")


def execute(command):
    completed = subprocess.run(command, capture_output=True, text=True, timeout=20, check=False)
    return {"command": command, "exitCode": completed.returncode,
            "stdout": completed.stdout, "stderr": completed.stderr}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--stock", required=True)
    parser.add_argument("--fork", required=True)
    parser.add_argument("--assembler", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=False)
    cases = []
    for operation in ("Load", "And", "Or"):
        cases.append(("wide-valid-" + operation, fixture(64, [BASE, EXTENDED], operation), False, True, "opencl2.2"))
    for label, declarations in (("missing-both", []), ("only-base", [BASE]), ("only-extended", [EXTENDED]),
                                ("unknown", ["cl_unknown_atomic_extension"]),
                                ("lookalike-base", [BASE + "_extra", EXTENDED]),
                                ("lookalike-extended", [BASE, EXTENDED + "_extra"]),
                                ("lookalike-case", [BASE.upper(), EXTENDED]),
                                ("lookalike-whitespace", [BASE + " ", EXTENDED])):
        cases.append((label, fixture(64, declarations), False, False, "opencl2.2"))
    valid = fixture(64, [BASE, EXTENDED])
    cases.extend([
        ("unrelated-Shader", fixture(64, [BASE, EXTENDED], extra_capability="OpCapability Shader\n"), False, False, "opencl2.2"),
        ("invalid-multiple-atomic-orders", valid.replace("%order = OpConstant %uint 16", "%order = OpConstant %uint 6"), False, False, "opencl2.2"),
        ("invalid-atomic-semantics-width", valid.replace("%order = OpConstant %uint 16", "%order = OpConstant %integer 16"), False, False, "opencl2.2"),
        ("invalid-pointer-type", valid.replace("%pointer_type = OpTypePointer CrossWorkgroup %integer", "%pointer_type = OpTypePointer CrossWorkgroup %uint"), False, False, "opencl2.2"),
        ("invalid-memory-scope", valid.replace("%scope = OpConstant %uint 1", "%scope = OpConstant %uint 99"), False, False, "opencl2.2"),
        ("unchanged-ordinary32", fixture(32, []), True, True, "opencl2.2"),
        ("unchanged-ordinary32-Shader", fixture(32, [], extra_capability="OpCapability Shader\n"), False, False, "opencl2.2"),
        ("unchanged-ordinary32-invalid-orders", fixture(32, []).replace("%order = OpConstant %integer 16", "%order = OpConstant %integer 6"), False, False, "opencl2.2"),
        ("does-not-enable-OpenCL12", valid, False, False, "opencl1.2"),
        ("does-not-enable-OpenCL21", valid, False, False, "opencl2.1"),
    ])
    for capability in ("Matrix", "Geometry", "Tessellation", "AtomicStorage", "InputAttachment", "Sampled1D"):
        cases.append(("unrelated-" + capability, fixture(32, [], extra_capability="OpCapability " + capability + "\n"), False, False, "opencl2.2"))
    rows = []
    for label, source, stock_expected, fork_expected, environment in cases:
        assembly = output / (label + ".spvasm")
        binary = output / (label + ".spv")
        assembly.write_text(source, encoding="utf-8")
        assembled = execute([args.assembler, "--target-env", "opencl2.2", str(assembly), "-o", str(binary)])
        if assembled["exitCode"] != 0:
            raise RuntimeError(json.dumps(assembled))
        stock = execute([args.stock, "--target-env", environment, str(binary)])
        fork = execute([args.fork, "--target-env", environment, str(binary)])
        passed = (stock["exitCode"] == 0) == stock_expected and (fork["exitCode"] == 0) == fork_expected
        rows.append({"case": label, "environment": environment, "stockExpected": stock_expected,
                     "forkExpected": fork_expected, "passed": passed, "assembly": assembled,
                     "stock": stock, "fork": fork, "moduleSha256": hashlib.sha256(binary.read_bytes()).hexdigest()})
        (output / "results.json").write_text(json.dumps({"identity": IDENTITY, "GPUExecution": False,
            "alignment": "SPIR-V atomic instructions do not encode dynamic pointer alignment; generated runtime alignment-fault9 witnesses remain required.",
            "rows": rows}, indent=2) + "\n", encoding="utf-8")
        if not passed:
            raise RuntimeError("Validator outcome mismatch: " + label + "\n" + json.dumps(rows[-1]))
    print(f"{IDENTITY}: {len(rows)} stock/fork OpenCL regression pairs passed")


if __name__ == "__main__":
    main()
