using System.Runtime.CompilerServices;

// Les modèles source bruts (entete_ba / lignes_ba / Regime_tva) et EncheresV6RowMapper sont INTERNES
// (encapsulation du plug-in — seuls EncheresV6FixtureExtractor et EncheresV6EmitterIdentity sont publics).
// Le projet de test accède à ces internes pour tester directement le mapper et la conversion
// flottant→decimal (acceptance ADP01). Ce n'est pas une fuite de frontière : aucune référence
// d'assembly plateforme n'est ouverte (AgentBoundaryTests reste vert).
[assembly: InternalsVisibleTo("Liakont.Agent.Adapters.EncheresV6.Tests")]
