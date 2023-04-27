using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CakeMachine.Fabrication.ContexteProduction;
using CakeMachine.Fabrication.Elements;
using CakeMachine.Fabrication.Opérations;
using CakeMachine.Utils;

namespace CakeMachine.Simulation.Algorithmes
{
    internal class AlgorithmeRendu : Algorithme
    {
        /// <inheritdoc />
        public override bool SupportsAsync => true;

        /// <inheritdoc />
        public override void ConfigurerUsine(IConfigurationUsine builder)
        {
            builder.NombrePréparateurs = 8;
            builder.NombreFours = 13;
            builder.NombreEmballeuses = 14;
        }

        private class OrdreProduction
        {
            private readonly Usine _usine;
            private readonly CancellationToken _token;
            private readonly IMachine<GâteauCuit, GâteauEmballé> _emballeuses;
            private readonly IMachine<GâteauCru[], GâteauCuit[]> _fours;
            private readonly IMachine<Plat, GâteauCru> _préparatrices;

            public OrdreProduction(Usine usine, CancellationToken token)
            {
                _usine = usine;
                _token = token;
                _emballeuses = usine.Emballeuses.PoolTogether();
                _fours = usine.Fours.PoolTogether();
                _préparatrices = usine.Préparateurs.PoolTogether();
            }

            public async IAsyncEnumerable<GâteauEmballé> ProduireAsync()
            {
                while (!_token.IsCancellationRequested)
                {
                    var tâchesEmballage = new List<Task<GâteauEmballé>>(
                        _usine.OrganisationUsine.ParamètresCuisson.NombrePlaces * _usine.OrganisationUsine.NombreFours
                    );

                    await foreach (var gâteauCuit in ProduireEtCuireParBains(
                                       _usine.OrganisationUsine.ParamètresCuisson.NombrePlaces, 13).WithCancellation(_token))tâchesEmballage.Add(_emballeuses.ProduireAsync(gâteauCuit, _token));

                    await foreach (var gâteauEmballé in tâchesEmballage.EnumerateCompleted().WithCancellation(_token))
                    {
                        if (!gâteauEmballé.EstConforme)
                        {
                            _usine.MettreAuRebut(gâteauEmballé);
                        }
                        else
                        {
                            yield return gâteauEmballé;
                        }
                    }
                }
            }

            private async IAsyncEnumerable<GâteauCuit> ProduireEtCuireParBains(
                int nombrePlacesParFour,
                int nombreBains)
            {
                var gâteauxCrus = PréparerConformesParBainAsync(nombrePlacesParFour, nombreBains);

                var tachesCuisson = new List<Task<GâteauCuit[]>>();
                await foreach (var bainGâteauxCrus in gâteauxCrus.WithCancellation(_token))
                    tachesCuisson.Add(_fours.ProduireAsync(bainGâteauxCrus, _token));

                await foreach (var bainGâteauxCuits in tachesCuisson.EnumerateCompleted().WithCancellation(_token))
                {
                    foreach (var gâteauCuit in bainGâteauxCuits)
                    {
                        if (!gâteauCuit.EstConforme)
                        {
                            _usine.MettreAuRebut(gâteauCuit);
                        }
                        else
                        {
                            yield return gâteauCuit;
                        }
                    }
                }
            }

            private async IAsyncEnumerable<GâteauCru[]> PréparerConformesParBainAsync(
                int gâteauxParBain, int bains)
            {
                var totalAPréparer = bains * gâteauxParBain;
                var gâteauxConformes = 0;
                var gâteauxRatés = 0;
                var gâteauxPrêts = new ConcurrentBag<GâteauCru>();

                async ValueTask  TakeNextAndSpawnChild(int depth)
                {
                    _token.ThrowIfCancellationRequested();
                    // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
                    while (depth >= totalAPréparer + gâteauxRatés)
                    {
                        _token.ThrowIfCancellationRequested();
                        if (gâteauxConformes == totalAPréparer) return;
                        await Task.Delay(_usine.OrganisationUsine.ParamètresPréparation.TempsMin / 2, _token).ConfigureAwait(false);
                    }

                    if (gâteauxConformes == totalAPréparer) return;

                    var child = TakeNextAndSpawnChild(depth + 1);
                    await PréparerPlat();
                    await child;
                }

                async Task PréparerPlat()
                {
                    _token.ThrowIfCancellationRequested();

                    var gateau = await _préparatrices.ProduireAsync(_usine.StockInfiniPlats.First(), _token);
                    if (gateau.EstConforme)
                    {
                        gâteauxPrêts!.Add(gateau);
                        Interlocked.Increment(ref gâteauxConformes);
                    }
                    else
                    {
                        _usine.MettreAuRebut(gateau);
                        Interlocked.Increment(ref gâteauxRatés);
                    };
                }

                var spawner = TakeNextAndSpawnChild(0);

                var buffer = new List<GâteauCru>(gâteauxParBain);
                for (var i = 0; i < totalAPréparer; i++)
                {
                    _token.ThrowIfCancellationRequested();

                    GâteauCru gâteauPrêt;

                    while (!gâteauxPrêts.TryTake(out gâteauPrêt!))
                    {
                        _token.ThrowIfCancellationRequested();
                        await Task.Delay(_usine.OrganisationUsine.ParamètresPréparation.TempsMin / 2, _token).ConfigureAwait(false);
                    }

                    buffer.Add(gâteauPrêt);

                    if (buffer.Count != gâteauxParBain) continue;

                    yield return buffer.ToArray();

                    buffer.Clear();
                }

                await spawner;
            }
        }

        /// <inheritdoc />
        public override async IAsyncEnumerable<GâteauEmballé> ProduireAsync(
            Usine usine,
            [EnumeratorCancellation] CancellationToken token)
        {
            var ligne = new OrdreProduction(usine, token);
            await foreach (var gâteauEmballé in ligne.ProduireAsync().WithCancellation(token))
                yield return gâteauEmballé;
        }
    }
}
