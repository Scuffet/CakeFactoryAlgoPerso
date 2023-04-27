using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CakeMachine.Simulation;
using CakeMachine.Simulation.Algorithmes;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace CakeMachine.Test;

public class TestAlgorithme
{
    private readonly ITestOutputHelper _testOutputHelper;

    public TestAlgorithme(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private static IEnumerable<Type> Algorithmes
        => Assembly.GetAssembly(typeof(Algorithme))!
            .GetTypes()
            .Where(type => type.BaseType == typeof(Algorithme));

    public static IEnumerable<object[]> CasTest => new CartesianData(Algorithmes, new [] { true, false });

    [Theory]
    [MemberData(nameof(CasTest))]
    [InlineData(typeof(AlgorithmeRendu), false)]
    public async Task TestNombreGâteauxEn5Secondes(Type algorithme, bool sync)
    {
        var runner = new SingleAlgorithmRunner(algorithme);
        var result = await runner.ProduirePendantAsync(TimeSpan.FromSeconds(5), sync);
            
        if(result is null) throw new XunitException("No algorithm");
        _testOutputHelper.WriteLine(result.ToString());
    }

    [Theory]
    [MemberData(nameof(CasTest))]
    public async Task TestTempsPour100Gateaux(Type algorithme, bool sync)
    {
        var runner = new SingleAlgorithmRunner(algorithme);
        var result = await runner.ProduireNGâteauxAsync(100, sync);

        if (result is null) throw new XunitException("No algorithm");
        _testOutputHelper.WriteLine(result.ToString());
    }
}