namespace Conformat.Gateway.Core
{
    /// <summary>
    /// Marqueur du module Core. Le produit générique (Pivot, TvaMapping, Validation,
    /// Tracking, PaClient, Pipeline, Configuration) sera implémenté par les lots PIV, TVA,
    /// VAL, TRK et CFG. Cette classe n'existe que pour matérialiser l'assembly et rendre les
    /// frontières de références vérifiables dès le socle (SOL01) — aucune logique métier.
    /// </summary>
    public static class CoreModule
    {
        /// <summary>
        /// Nom logique du module. Exposé via une propriété (et non une constante) pour que les
        /// tests de fumée chargent réellement l'assembly à l'exécution plutôt qu'une valeur inlinée.
        /// </summary>
        public static string Name => "Gateway.Core";
    }
}
