This recipe builds `warpclr-spirv-val-int64-0.1` from pinned Khronos SPIRV-Tools v2025.1 and the exact upstream SPIRV-Headers DEPS revision. Python 3.12+, CMake, a C++ compiler, a build tool, and `patch` are required. SHA256 checks cover both downloaded archives, DEPS, the patch, and the two changed source files. The output records the actual executable hash and full source inventory. Compiler and build metadata can change the executable hash; the runtime binds the executable actually selected to each toolchain/cache identity.

Build from the installed NuGet package's `tools/portable-validation` directory:

```sh
python3 build-validator.py --output /absolute/new-validator-directory
```

Set `WarpNativeRuntimeOptions.SpirVValidatorPath` to the absolute path of the resulting `warpclr-spirv-val-int64-0.1` executable. The runtime requires its explicit `warpclr.opencl-int64-atomic-extensions/0.1` version identity for modules that use native 64-bit atomics. It hashes the executable into native artifact/cache identity. The default stock validator continues validating ordinary modules and rejects this wider binding. There is no automatic download, tool substitution, or universal-environment fallback during runtime compilation.

The conditional rule applies solely to capability `Int64Atomics` in OpenCL 2.2 full/embedded validation when the module declares both exact strings `cl_khr_int64_base_atomics` and `cl_khr_int64_extended_atomics`. Every other validator pass remains unchanged. The generated module adds those declarations only when an admitted operation requires native 64-bit atomics. Device admission separately queries both runtime extension tokens and little-endian storage before effects. An offline validation result establishes no device support or GPU execution.

The rule follows [OpenCL SPIR-V Environment section 5.2.8](https://registry.khronos.org/OpenCL/specs/unified/html/OpenCL_Env.html). The unchanged upstream baseline is [SPIRV-Tools v2025.1](https://github.com/KhronosGroup/SPIRV-Tools/tree/v2025.1). Archive/header/patch hashes and the validated local artifact are in `validator.lock.json`; the patch and upstream licenses are distributed alongside this recipe.

Run the included regression recipe with an unchanged stock validator, the named validator, and `spirv-as`:

```sh
python3 validator-regressions.py --stock /absolute/stock-spirv-val --fork /absolute/warpclr-spirv-val-int64-0.1 --assembler /absolute/spirv-as --output /absolute/new-regression-directory
```

It checks both required declarations, base and extended operations, missing/unknown/lookalike strings, unrelated Shader capability, invalid atomic ordering/pointer type/scope, ordinary 32-bit acceptance, and unchanged older OpenCL environments. Dynamic physical alignment is verified by the generated kernel before memory effects; SPIR-V atomic instructions do not encode the executing pointer's alignment.
