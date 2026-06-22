namespace Liakont.PaClients.ChorusPro.Wire;

using System.Text.Json.Serialization;

/// <summary>
/// DTO « fil » du payload <c>deposerFluxFacture</c> (F18 §3.1). Les noms JSON sont en <b>camelCase EXACT</b>
/// imposés par Chorus Pro (<c>fichierFlux</c>, <c>nomFichier</c>, <c>syntaxeFlux</c>, <c>avecSignature</c>) —
/// fixés ici par <see cref="JsonPropertyNameAttribute"/> pour ne PAS subir la politique <c>snake_case</c>
/// d'écriture de <see cref="ChorusProJson.Options"/> (utilisée par la réponse OAuth2). <c>internal</c> :
/// aucun type « fil » Chorus Pro n'est exposé hors de l'assembly (acceptance CP02, vérifié par
/// <c>ChorusProBoundaryTests</c>).
/// <para>
/// <c>idUtilisateurCourant</c> est volontairement ABSENT : sa cardinalité / son caractère requis ne sont pas
/// confirmés au Swagger PISTE (F18 §3.2, « À VERROUILLER »). Conformément à la branche documentée « SINON
/// l'omettre » et à CLAUDE.md n°2 (ne jamais inventer un champ/endpoint non tracé), le champ — et l'appel de
/// résolution <c>consulterCompteUtilisateur</c> — sont différés au raccordement (s'il s'avère requis).
/// Aucun montant n'apparaît dans le payload : transport PUR de l'artefact scellé.
/// </para>
/// </summary>
/// <param name="FichierFlux">Base64 du PDF/A-3 Factur-X scellé (porté par <c>PaSendContext</c>, FX07).</param>
/// <param name="NomFichier">Nom de fichier du flux déposé (assaini depuis le numéro de document).</param>
/// <param name="SyntaxeFlux">Syntaxe du flux — <see cref="ChorusProDefaults.SyntaxeFluxFacturX"/> (✅ F18 §3.1).</param>
/// <param name="AvecSignature">Drapeau de signature — <c>false</c> (artefact non signé, D9 / F18 §6).</param>
internal sealed record ChorusProDepositRequest(
    [property: JsonPropertyName("fichierFlux")] string FichierFlux,
    [property: JsonPropertyName("nomFichier")] string NomFichier,
    [property: JsonPropertyName("syntaxeFlux")] string SyntaxeFlux,
    [property: JsonPropertyName("avecSignature")] bool AvecSignature);
