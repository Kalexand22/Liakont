namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Lit le profil intégrateur EMBARQUÉ dans l'installeur (F13 §7, OPS08c). Le packaging multi-profils
/// produit « un .exe par intégrateur à partir du même binaire » en injectant le manifeste du profil
/// (JSON UTF-8) comme ressource Win32 <c>RT_RCDATA</c> nommée <see cref="ResourceName"/> dans une COPIE
/// de l'installeur déjà construit — aucun recompilation par intégrateur (cf. tools/package-installer.ps1).
/// <para>
/// La ressource vit À L'INTÉRIEUR du PE (et non en fichier annexe éditable), ce qui sert l'intégrité du
/// profil (F13 §5.5 : un intégrateur ne peut pas ré-afficher un écran masqué en éditant un fichier ; une
/// signature Authenticode appliquée APRÈS l'injection couvrirait la ressource). Un installeur de
/// développement non packagé n'a pas cette ressource : <see cref="TryReadEmbeddedJson"/> rend alors
/// <c>null</c> et l'installeur retombe sur le profil par défaut ouvert (F13 §5.3) — comportement inchangé.
/// </para>
/// L'interprétation et la validation du JSON relèvent de <see cref="IntegratorProfileLoader"/> et
/// <see cref="ProfileValidator"/> (réutilisés, non dupliqués) ; cette classe ne fait QUE l'extraction des
/// octets de la ressource du module courant. C'est l'unique arête liée à la machine (P/Invoke sur le PE
/// chargé) — éprouvée par le round-trip du self-test de packaging (tools/test-installer-packaging.ps1),
/// pas par un test unitaire (au même titre que le déployeur de service, machine-bound).
/// </summary>
internal static class EmbeddedProfile
{
    /// <summary>
    /// Nom (chaîne, donc stocké en MAJUSCULES par le chargeur de ressources Windows) de la ressource
    /// <c>RT_RCDATA</c> qui porte le manifeste JSON du profil. Doit rester identique à celui qu'injecte
    /// tools/package-installer.ps1.
    /// </summary>
    public const string ResourceName = "LIAKONTPROFILE";

    // Langue neutre (0) à l'injection ET à la lecture : appariement déterministe, sans dépendre de la
    // langue du thread.
    private const ushort LangNeutral = 0;

    // RT_RCDATA = 10 (donnée binaire brute, MAKEINTRESOURCE(10)) ; un IntPtr ne peut pas être const.
    private static readonly IntPtr RtRcData = new IntPtr(10);

    /// <summary>
    /// Rend le manifeste JSON du profil embarqué dans CET exécutable, ou <c>null</c> si aucun profil n'est
    /// embarqué (installeur de développement, ou paquet sans profil). N'interprète pas le JSON.
    /// </summary>
    public static string? TryReadEmbeddedJson()
    {
        // Module du processus courant (l'exe lui-même) : null = exécutable principal.
        IntPtr hModule = GetModuleHandle(null);
        if (hModule == IntPtr.Zero)
        {
            return null;
        }

        IntPtr hResInfo = FindResourceEx(hModule, RtRcData, ResourceName, LangNeutral);
        if (hResInfo == IntPtr.Zero)
        {
            return null;
        }

        uint size = SizeofResource(hModule, hResInfo);
        if (size == 0)
        {
            return null;
        }

        IntPtr hResData = LoadResource(hModule, hResInfo);
        if (hResData == IntPtr.Zero)
        {
            return null;
        }

        IntPtr pointer = LockResource(hResData);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, (int)size);

        // Décodage UTF-8 sans BOM (l'injection écrit sans BOM) ; on retire défensivement un éventuel BOM
        // de tête (U+FEFF) pour que Newtonsoft.Json ne bute pas dessus.
        string json = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
        if (json.Length > 0 && json[0] == (char)0xFEFF)
        {
            json = json.Substring(1);
        }

        return json;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll", EntryPoint = "FindResourceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FindResourceEx(
        IntPtr hModule,
        IntPtr lpType,
        [MarshalAs(UnmanagedType.LPWStr)] string lpName,
        ushort wLanguage);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LockResource(IntPtr hResData);
}
