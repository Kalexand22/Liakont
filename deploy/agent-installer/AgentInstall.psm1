<#
.SYNOPSIS
    Fonctions partagées de l'installeur de l'agent Liakont (OPS05).
.DESCRIPTION
    Module commun à tools/package-agent.ps1 (poste de build), à install-agent.ps1 /
    uninstall-agent.ps1 (poste cible, embarqués dans le package) et aux tests Pester.

    Il centralise ce qui DOIT rester cohérent entre la fabrication du package et son
    installation :
      - la dérivation des noms et chemins d'une INSTANCE (multi-instances, OPS05 pt 5) ;
      - le type machine PE d'un binaire (contrôle x86/x64 du package) ;
      - le transport sécurisé de la clé API de pré-configuration (chiffrement par mot de
        passe à usage unique, décision documentée dans docs/adr/ADR-0019).

    AUTORITÉ DE VALIDATION : la règle de nommage d'instance est portée, côté exécution, par
    Liakont.Agent.Core.AgentInstance.TryParse (C#). Les RÈGLES de validation ci-dessous en sont le
    miroir (même expression rationnelle, mêmes noms réservés), servant de pré-contrôle fail-fast :
    un nom rejeté ici ne touche jamais le système de fichiers ni le SCM. Une différence VOULUE :
    une chaîne vide/blanche est normalisée vers « Default » (convenance du paramètre -InstanceName,
    qui vaut « Default » par défaut), là où le C# rejette une valeur --instance vide explicite — le
    script ne transmet jamais de nom vide au binaire. Le binaire reste l'autorité finale (il
    revalide à « Liakont.Agent.exe install --instance <nom> »).

    Messages opérateur en français (CLAUDE.md n°12).
#>

Set-StrictMode -Version Latest

# Miroir de AgentInstance.NamePattern (C#) : alphanumérique en tête, puis lettres/chiffres/'-'/'_',
# 32 caractères maximum — le nom entre dans un nom de service Windows, un mutex et un répertoire.
$script:InstanceNamePattern = '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$'

# Miroir de AgentInstance.ReservedNames (C#) : sous-objets de l'instance Default vivant à la racine
# partagée %ProgramData%\Liakont, et périphériques réservés Windows (invalides comme répertoire).
$script:InstanceReservedNames = @(
    'logs', 'update-work',
    'CON', 'PRN', 'AUX', 'NUL',
    'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9',
    'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9'
)

$script:DefaultInstanceName = 'Default'

function Resolve-AgentInstance {
    <#
    .SYNOPSIS
        Valide un nom d'instance et rend ses dérivations (service, mutex, répertoire de données).
    .DESCRIPTION
        Reprend les règles de validation de Liakont.Agent.Core.AgentInstance (regex, noms réservés).
        Une chaîne vide/blanche OU « Default » (toute casse) est normalisée vers l'instance par défaut,
        qui conserve STRICTEMENT les noms et chemins historiques (la chaîne vide est une convenance du
        paramètre -InstanceName, voir l'en-tête du module). Lève une exception avec un message français
        orienté intégrateur en cas de nom invalide ou réservé.
    .OUTPUTS
        PSCustomObject { Name; IsDefault; ServiceName; DisplayName; RunMutexName; DataDirectory }
    #>
    [CmdletBinding()]
    param(
        [string]$Name
    )

    $trimmed = ([string]$Name).Trim()

    if ([string]::Equals($trimmed, $script:DefaultInstanceName, [System.StringComparison]::OrdinalIgnoreCase) -or
        $trimmed.Length -eq 0) {
        $canonical = $script:DefaultInstanceName
        $isDefault = $true
    }
    else {
        if ($trimmed -notmatch $script:InstanceNamePattern) {
            throw "Nom d'instance invalide : « $trimmed ». Règle : lettres ou chiffres en première " +
                  "position, puis lettres, chiffres, « - » ou « _ », 32 caractères maximum."
        }

        foreach ($reserved in $script:InstanceReservedNames) {
            if ([string]::Equals($reserved, $trimmed, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Nom d'instance réservé : « $trimmed ». Choisissez un autre nom."
            }
        }

        $canonical = $trimmed
        $isDefault = $false
    }

    $programData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::CommonApplicationData)
    $root = Join-Path $programData 'Liakont'
    $dataDirectory = if ($isDefault) { $root } else { Join-Path $root $canonical }

    # Mutex de run par instance : composante canonicalisée en MAJUSCULES (les mutex nommés sont
    # sensibles à la casse, les chemins Windows non — voir AgentInstance.RunMutexName).
    $defaultMutex = 'Global\LiakontAgentRun'
    $runMutex = if ($isDefault) { $defaultMutex } else { $defaultMutex + '-' + $canonical.ToUpperInvariant() }

    return [PSCustomObject]@{
        Name          = $canonical
        IsDefault     = $isDefault
        ServiceName   = if ($isDefault) { 'LiakontAgent' } else { 'LiakontAgent$' + $canonical }
        DisplayName   = if ($isDefault) { 'Liakont Agent' } else { "Liakont Agent ($canonical)" }
        RunMutexName  = $runMutex
        DataDirectory = $dataDirectory
    }
}

function Get-PeMachineType {
    <#
    .SYNOPSIS
        Rend le type machine d'un binaire PE (« x86 », « x64 » ou « unknown »).
    .DESCRIPTION
        Lit l'en-tête PE (champ Machine du COFF header). Sert au contrôle anti-faux-vert du
        packaging : l'EXE du package x86 DOIT être 32 bits (pour charger un driver ODBC 32 bits),
        celui du package x64 DOIT être 64 bits, et le SQLite.Interop.dll embarqué doit correspondre.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Binaire introuvable pour le contrôle PE : « $Path »."
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Seek(0x3C, [System.IO.SeekOrigin]::Begin) | Out-Null
        $peOffset = $reader.ReadInt32()
        $stream.Seek($peOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $signature = $reader.ReadUInt32()        # 'PE\0\0' = 0x00004550
        if ($signature -ne 0x00004550) {
            return 'unknown'
        }

        $machine = $reader.ReadUInt16()
        switch ($machine) {
            0x014C { return 'x86' }   # IMAGE_FILE_MACHINE_I386
            0x8664 { return 'x64' }   # IMAGE_FILE_MACHINE_AMD64
            default { return 'unknown' }
        }
    }
    finally {
        $stream.Close()
    }
}

function Get-AgentBinaryArchitecture {
    <#
    .SYNOPSIS
        Rend l'architecture d'un binaire (« x86 », « x64 », « anycpu », « native » ou « unknown »).
    .DESCRIPTION
        Pour un assembly MANAGÉ (EXE/DLL .NET), le champ Machine de l'en-tête PE rapporte I386 AUSSI
        BIEN pour x86 (32BITREQUIRED) que pour AnyCPU — <see cref="Get-PeMachineType"/> ne les distingue
        donc pas. On lit donc directement le PE : le champ Machine + l'en-tête CLR (IMAGE_COR20_HEADER)
        et son drapeau COMIMAGE_FLAGS_32BITREQUIRED.
        <para>
        Cette lecture d'octets est IDENTIQUE en .NET Framework (Windows PowerShell 5.1) et en .NET
        (PowerShell 7 / pwsh, runtime de la CI) — contrairement à AssemblyName.ProcessorArchitecture,
        obsolète sous .NET Core+ (SYSLIB0037) où elle rend « None » pour tout assembly.
        </para>
        <para>
        x64 (Machine AMD64, managé) → « x64 » ; Machine I386 + CLR + 32BITREQUIRED → « x86 » ;
        Machine I386 + CLR sans 32BITREQUIRED → « anycpu » ; pas d'en-tête CLR → « native ».
        </para>
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Binaire introuvable pour le contrôle d'architecture : « $Path »."
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)

        $stream.Seek(0x3C, [System.IO.SeekOrigin]::Begin) | Out-Null
        $peOffset = $reader.ReadInt32()
        $stream.Seek($peOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
        if ($reader.ReadUInt32() -ne 0x00004550) { return 'unknown' }   # 'PE\0\0'

        # En-tête COFF (20 octets).
        $machine = $reader.ReadUInt16()
        $numSections = $reader.ReadUInt16()
        $reader.ReadUInt32() | Out-Null   # TimeDateStamp
        $reader.ReadUInt32() | Out-Null   # PointerToSymbolTable
        $reader.ReadUInt32() | Out-Null   # NumberOfSymbols
        $sizeOptionalHeader = $reader.ReadUInt16()
        $reader.ReadUInt16() | Out-Null   # Characteristics

        $optionalHeaderStart = $stream.Position           # peOffset + 24
        $magic = $reader.ReadUInt16()                     # 0x10B = PE32, 0x20B = PE32+
        # Répertoire de données : index 14 = en-tête CLR (COM descriptor).
        $dataDirOffset = if ($magic -eq 0x20B) { $optionalHeaderStart + 112 } else { $optionalHeaderStart + 96 }
        $stream.Seek($dataDirOffset + (14 * 8), [System.IO.SeekOrigin]::Begin) | Out-Null
        $cliRva = $reader.ReadUInt32()
        $reader.ReadUInt32() | Out-Null                   # taille (non utilisée)
        if ($cliRva -eq 0) { return 'native' }            # pas d'en-tête CLR → binaire natif

        # Mappe la RVA de l'en-tête CLR vers un offset fichier via les en-têtes de section (40 octets).
        $sectionHeaderStart = $optionalHeaderStart + $sizeOptionalHeader
        $cliFileOffset = 0
        for ($i = 0; $i -lt $numSections; $i++) {
            $stream.Seek($sectionHeaderStart + ($i * 40) + 8, [System.IO.SeekOrigin]::Begin) | Out-Null
            $virtualSize = $reader.ReadUInt32()
            $virtualAddress = $reader.ReadUInt32()
            $reader.ReadUInt32() | Out-Null               # SizeOfRawData
            $pointerToRawData = $reader.ReadUInt32()
            $span = if ($virtualSize -gt 0) { $virtualSize } else { $reader.ReadUInt32() }
            if ($cliRva -ge $virtualAddress -and $cliRva -lt ($virtualAddress + $span)) {
                $cliFileOffset = $cliRva - $virtualAddress + $pointerToRawData
                break
            }
        }
        if ($cliFileOffset -eq 0) { return 'unknown' }

        # IMAGE_COR20_HEADER.Flags est à l'offset +16 ; COMIMAGE_FLAGS_32BITREQUIRED = 0x2.
        $stream.Seek($cliFileOffset + 16, [System.IO.SeekOrigin]::Begin) | Out-Null
        $corFlags = $reader.ReadUInt32()
        $requires32Bit = ($corFlags -band 0x2) -ne 0

        switch ($machine) {
            0x8664 { return 'x64' }                                            # AMD64
            0x014C { if ($requires32Bit) { return 'x86' } else { return 'anycpu' } }   # I386
            default { return 'unknown' }
        }
    }
    finally {
        $stream.Close()
    }
}

function Protect-AgentPreConfigSecret {
    <#
    .SYNOPSIS
        Chiffre un secret de pré-configuration (clé API) par mot de passe à usage unique.
    .DESCRIPTION
        Transport sécurisé de la clé API dans un package PRÉ-CONFIGURÉ (OPS05 pt 3, ADR-0019) :
        la clé n'apparaît JAMAIS en clair dans le package. Schéma « chiffrer-puis-MAC » :
        PBKDF2-SHA256 (clé de chiffrement + clé MAC dérivées du mot de passe), AES-256-CBC,
        HMAC-SHA256 sur (iv || ciphertext). Le mot de passe à usage unique est communiqué
        SÉPARÉMENT (hors package), jamais versionné, jamais journalisé.
    .OUTPUTS
        PSCustomObject décrivant le secret protégé (champs base64), sérialisable en JSON.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlainText,

        [Parameter(Mandatory = $true)]
        [string]$Password,

        [int]$Iterations = 200000
    )

    $salt = New-Object byte[] 16
    $iv = New-Object byte[] 16
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($salt)
        $rng.GetBytes($iv)
    }
    finally {
        $rng.Dispose()
    }

    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $Password, $salt, $Iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $encKey = $kdf.GetBytes(32)
        $macKey = $kdf.GetBytes(32)
    }
    finally {
        $kdf.Dispose()
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $encKey
        $aes.IV = $iv
        $encryptor = $aes.CreateEncryptor()
        try {
            $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($PlainText)
            $cipherBytes = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    # MAC sur (iv || ciphertext) — l'intégrité couvre l'IV ET le chiffré (encrypt-then-MAC).
    $macInput = New-Object byte[] ($iv.Length + $cipherBytes.Length)
    [System.Array]::Copy($iv, 0, $macInput, 0, $iv.Length)
    [System.Array]::Copy($cipherBytes, 0, $macInput, $iv.Length, $cipherBytes.Length)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256(, $macKey)
    try {
        $mac = $hmac.ComputeHash($macInput)
    }
    finally {
        $hmac.Dispose()
    }

    return [PSCustomObject]@{
        v          = 1
        kdf        = 'PBKDF2-SHA256'
        iterations = $Iterations
        cipher     = 'AES-256-CBC'
        mac        = 'HMAC-SHA256'
        salt       = [System.Convert]::ToBase64String($salt)
        iv         = [System.Convert]::ToBase64String($iv)
        ciphertext = [System.Convert]::ToBase64String($cipherBytes)
        tag        = [System.Convert]::ToBase64String($mac)
    }
}

function Unprotect-AgentPreConfigSecret {
    <#
    .SYNOPSIS
        Déchiffre un secret de pré-configuration avec le mot de passe à usage unique.
    .DESCRIPTION
        Vérifie le HMAC AVANT de déchiffrer (rejet d'un mot de passe erroné ou d'un package
        altéré, en temps constant). Lève une exception française si le contrôle échoue.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Secret,

        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    $salt = [System.Convert]::FromBase64String($Secret.salt)
    $iv = [System.Convert]::FromBase64String($Secret.iv)
    $cipherBytes = [System.Convert]::FromBase64String($Secret.ciphertext)
    $expectedMac = [System.Convert]::FromBase64String($Secret.tag)
    $iterations = [int]$Secret.iterations

    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $Password, $salt, $iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $encKey = $kdf.GetBytes(32)
        $macKey = $kdf.GetBytes(32)
    }
    finally {
        $kdf.Dispose()
    }

    $macInput = New-Object byte[] ($iv.Length + $cipherBytes.Length)
    [System.Array]::Copy($iv, 0, $macInput, 0, $iv.Length)
    [System.Array]::Copy($cipherBytes, 0, $macInput, $iv.Length, $cipherBytes.Length)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256(, $macKey)
    try {
        $actualMac = $hmac.ComputeHash($macInput)
    }
    finally {
        $hmac.Dispose()
    }

    if (-not (Compare-ConstantTime $expectedMac $actualMac)) {
        throw "Mot de passe de pré-configuration incorrect ou package de pré-configuration altéré " +
              "(contrôle d'intégrité en échec). Vérifiez le mot de passe à usage unique communiqué " +
              "séparément."
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $encKey
        $aes.IV = $iv
        $decryptor = $aes.CreateDecryptor()
        try {
            $plainBytes = $decryptor.TransformFinalBlock($cipherBytes, 0, $cipherBytes.Length)
        }
        finally {
            $decryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    return [System.Text.Encoding]::UTF8.GetString($plainBytes)
}

function Compare-ConstantTime {
    <#
    .SYNOPSIS
        Comparaison d'octets en temps constant (évite une fuite par canal temporel sur le MAC).
    #>
    [CmdletBinding()]
    param(
        [byte[]]$Left,
        [byte[]]$Right
    )

    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) {
        return $false
    }

    $diff = 0
    for ($i = 0; $i -lt $Left.Length; $i++) {
        $diff = $diff -bor ($Left[$i] -bxor $Right[$i])
    }

    return $diff -eq 0
}

function New-AgentOneTimePassword {
    <#
    .SYNOPSIS
        Génère un mot de passe à usage unique fort pour le transport de la clé de pré-configuration.
    .DESCRIPTION
        Alphabet base32 sans caractères ambigus (ni 0/O ni 1/I/L), groupé en blocs pour la dictée
        à l'oral lors de la communication SÉPARÉE du mot de passe à l'intégrateur.
    #>
    [CmdletBinding()]
    param(
        [int]$Groups = 5,
        [int]$GroupSize = 5
    )

    $alphabet = '23456789ABCDEFGHJKMNPQRSTUVWXYZ'
    $alphabetLen = $alphabet.Length
    $total = $Groups * $GroupSize

    # Rejection sampling : 256 n'est pas un multiple de 31, donc un simple « octet % 31 » biaiserait
    # le tirage vers les premiers symboles. On rejette les octets >= (256 - 256 % 31) avant la
    # réduction modulo → chaque symbole du secret est équiprobable.
    $limit = 256 - (256 % $alphabetLen)
    $chars = New-Object char[] $total
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $produced = 0
        $buffer = New-Object byte[] ($total * 2)
        while ($produced -lt $total) {
            $rng.GetBytes($buffer)
            foreach ($b in $buffer) {
                if ($b -lt $limit) {
                    $chars[$produced] = $alphabet[$b % $alphabetLen]
                    $produced++
                    if ($produced -eq $total) { break }
                }
            }
        }
    }
    finally {
        $rng.Dispose()
    }

    # Variable locale distincte de $Groups (PowerShell est insensible à la casse : $groups ≡ $Groups,
    # typé [int] — réutiliser le nom casserait la conversion du tableau de groupes).
    $blocks = New-Object System.Collections.Generic.List[string]
    for ($g = 0; $g -lt $Groups; $g++) {
        $blocks.Add(-join $chars[($g * $GroupSize)..(($g * $GroupSize) + $GroupSize - 1)])
    }
    return ($blocks -join '-')
}

Export-ModuleMember -Function Resolve-AgentInstance, Get-PeMachineType, Get-AgentBinaryArchitecture,
    Protect-AgentPreConfigSecret, Unprotect-AgentPreConfigSecret, Compare-ConstantTime,
    New-AgentOneTimePassword
