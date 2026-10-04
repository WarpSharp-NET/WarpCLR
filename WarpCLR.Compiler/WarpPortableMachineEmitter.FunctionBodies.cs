using WarpCLR.IR;

namespace WarpCLR.Compiler;

public sealed partial class WarpPortableMachineEmitter
{
    private sealed partial class Emission
    {
        private string BodyCallingConvention => backend == WarpBackendKind.SPIRV ? "spir_func " : string.Empty;

        private string QuantumPointerType => backend == WarpBackendKind.AMDGPU ? "ptr addrspace(5)" : "ptr";

        private void AppendFunctionDispatch()
        {
            Line("dispatch:");
            AppendDispatchFrame();
            string function = LoadFrame(WarpLogicalMachineLayout.FrameFunctionOffset);
            Line($"  switch i32 {function}, label %invalid_state [");
            for (int body = 0; body <= layout.Kernel.Functions.Count; body++)
            {
                Line($"    i32 {N(body)}, label %body_{N(body)}");
            }
            Line("  ]");
            for (int body = 0; body <= layout.Kernel.Functions.Count; body++)
            {
                Line($"body_{N(body)}:");
                string proceed = Assign($"call {BodyCallingConvention}i1 @warp_body_{N(body)}({BodyParameters()})");
                Line($"  br i1 {proceed}, label %dispatch, label %done");
            }
            AppendInvalidState();
        }

        private string BodyParameters()
        {
            var parameters = new List<string>
            {
                "ptr addrspace(1) %state", $"{QuantumPointerType} %quantum_ptr", "i32 %warp_max_depth",
                "i32 %physical_max_depth", "i64 %frame_area", "i64 %input_index", "i64 %stride",
            };
            for (int index = 0; index < layout.Kernel.InputBufferCount; index++)
            {
                parameters.Add($"ptr addrspace(1) %warp_input_{N(index)}");
            }
            for (int index = 0; index < layout.Kernel.ScalarArgumentCount; index++)
            {
                parameters.Add($"i32 %warp_scalar_{N(index)}");
            }
            if (layout.RequiresManagedMemory)
            {
                parameters.AddRange(["ptr addrspace(1) %warp_heap", "i32 %warp_heap_words"]);
            }
            return string.Join(", ", parameters);
        }

        private void AppendFunctionBody(int function)
        {
            Line($"define internal {BodyCallingConvention}i1 @warp_body_{N(function)}({BodyParameters()}) #1 {{");
            Line("entry:");
            Line("  br label %dispatch");
            Line("dispatch:");
            AppendDispatchFrame();
            string current = LoadFrame(WarpLogicalMachineLayout.FrameFunctionOffset);
            string same = Assign($"icmp eq i32 {current}, {N(function)}");
            Line($"  br i1 {same}, label %dispatch_pc, label %changed_function");
            Line("changed_function:");
            Line("  ret i1 true");
            Line("dispatch_pc:");
            WarpLogicalMachineNode[] body = layout.Nodes.Where(node => node.Function == function).ToArray();
            string pc = LoadFrame(WarpLogicalMachineLayout.FrameProgramCounterOffset);
            AppendProgramCounterSwitch(pc, body);
            AppendInvalidState();
            foreach (WarpLogicalMachineNode node in body) { AppendNode(node); }
            Line("done:");
            Line("  ret i1 false");
            Line("}");
        }
    }
}
