using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
[assembly: DiscoverInternals]
[assembly: AssemblyMetadata("WarpCIL.Manifest", """{"contract":"warpcil/0.1","producer":"WarpCLR.Tests","producerVersion":"0.1.0","entries":[{"type":"WarpCLR.Tests.TestKernels","method":"ManifestMap","execution":"map","parameterRoles":["input","scalar"],"capabilities":["warp.core.scalar/0.1","warp.core.parallel/0.1","warp.core.buffers/0.1","warp.core.control-flow/0.2","warp.core.calls/0.1"],"graphHash":"21F1C372D38457F7860E6E202D3218D5572A8AAFECAABD7C2D20E7F06155E750"},{"type":"WarpCLR.Tests.TestKernels","method":"ManifestReduction","execution":"reduce-wrapping-sum","parameterRoles":["input","scalar"],"capabilities":["warp.core.scalar/0.1","warp.core.parallel/0.1","warp.core.buffers/0.1","warp.core.control-flow/0.2","warp.core.calls/0.1"],"graphHash":"32DAA260F58DE8F2A529ECB7F53D8FBA2A81C6FFF5DF343CDB222AAB0F68D3FB"}],"hostImports":[],"extensions":[]}""")]
