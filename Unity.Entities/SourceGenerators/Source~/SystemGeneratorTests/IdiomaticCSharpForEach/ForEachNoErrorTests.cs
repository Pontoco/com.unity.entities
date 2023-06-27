using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS =
    Unity.Entities.SourceGenerators.Test.CSharpSourceGeneratorVerifier<
        Unity.Entities.SourceGen.SystemGenerator.SystemGenerator>;

namespace Unity.Entities.SourceGenerators;

[TestClass]
public class ForEachNoErrorTests
{
    [TestMethod]
    public async Task ForEachIteration_InSystemBase()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial class SomeSystem : SystemBase {
                protected override void OnUpdate() {
                    foreach (var aspect in SystemAPI.Query<EcsTestAspect>()) {}
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task DifferentAssemblies_Aspect()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    foreach (var aspect in SystemAPI.Query<EcsTestAspect>()){}
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task DifferentAssemblies_ComponentDataRef()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    foreach (var data in SystemAPI.Query<RefRW<EcsTestData>>()){}
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task DifferentAssemblies_Combined()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    foreach (var (aspect, data) in SystemAPI.Query<EcsTestAspect, RefRW<EcsTestData>>()){}
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task SystemBasePartialTypes_IdiomaticForEach()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial class UserWrittenPartial : SystemBase {
                protected override void OnUpdate() {
                    foreach (var (data, data1) in SystemAPI.Query<RefRO<EcsTestData>, RefRO<EcsTestData2>>())
                    {
                        OnUpdate(unusedParameter: true);
                    }
                }
            }

            public partial class UserWrittenPartial : SystemBase {
                protected void OnUpdate(bool unusedParameter) {
                    foreach (var (data, data1) in SystemAPI.Query<RefRO<EcsTestData>, RefRO<EcsTestData2>>())
                    {
                    }
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task ISystemPartialTypes_IdiomaticForEach()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial struct UserWrittenPartial : ISystem {
                public void OnUpdate(ref SystemState state) {
                    foreach (var (data, data1) in SystemAPI.Query<RefRO<EcsTestData>, RefRO<EcsTestData2>>())
                    {
                        OnUpdate(unusedParameter: true, ref state);
                    }
                }
            }

            public partial struct UserWrittenPartial : ISystem {
                public void OnUpdate(bool unusedParameter, ref SystemState state) {
                    foreach (var (data, data1) in SystemAPI.Query<RefRO<EcsTestData>, RefRO<EcsTestData2>>())
                    {
                    }
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }
}
