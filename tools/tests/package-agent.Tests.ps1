<#
.SYNOPSIS
    Tests Pester des fonctions de packaging/installation de l'agent (OPS05).
.DESCRIPTION
    Couvre la LOGIQUE PURE du module AgentInstall.psm1 (dérivation d'instance, contrôle PE x86/x64,
    transport chiffré de la clé de pré-configuration) — la partie testable sans installer de service
    Windows. L'assemblage réel des packages est exercé par le node smoke_test (exécution de
    tools/package-agent.ps1) ; l'installation/coexistence sur machine est validée à GATE_TOOLKIT.

    Exécution : Invoke-Pester -Path tools/tests/package-agent.Tests.ps1
#>

$modulePath = Join-Path $PSScriptRoot '..\..\deploy\agent-installer\AgentInstall.psm1'
Import-Module $modulePath -Force

Describe 'Resolve-AgentInstance' {

    Context 'Instance par défaut' {
        It 'normalise une chaîne vide vers Default avec les noms et chemins historiques' {
            $r = Resolve-AgentInstance -Name ''
            $r.Name | Should Be 'Default'
            $r.IsDefault | Should Be $true
            $r.ServiceName | Should Be 'LiakontAgent'
            $r.DisplayName | Should Be 'Liakont Agent'
            $r.RunMutexName | Should Be 'Global\LiakontAgentRun'
            $r.DataDirectory | Should Match 'Liakont$'
        }

        It 'normalise « default » (toute casse) vers Default' {
            (Resolve-AgentInstance -Name 'DEFAULT').IsDefault | Should Be $true
            (Resolve-AgentInstance -Name 'default').ServiceName | Should Be 'LiakontAgent'
        }
    }

    Context 'Instance nommée' {
        It 'dérive un service, un mutex et un répertoire propres' {
            $r = Resolve-AgentInstance -Name 'ClientA'
            $r.IsDefault | Should Be $false
            $r.ServiceName | Should Be 'LiakontAgent$ClientA'
            $r.DisplayName | Should Be 'Liakont Agent (ClientA)'
            $r.DataDirectory | Should Match 'Liakont\\ClientA$'
        }

        It 'canonicalise la composante mutex en majuscules (insensibilité de casse des chemins)' {
            (Resolve-AgentInstance -Name 'ClientA').RunMutexName | Should Be 'Global\LiakontAgentRun-CLIENTA'
        }
    }

    Context 'Noms rejetés (miroir strict de AgentInstance.cs)' {
        It 'rejette un nom à caractère invalide' {
            { Resolve-AgentInstance -Name 'mauvais nom' } | Should Throw
        }
        It 'rejette un nom commençant par un tiret' {
            { Resolve-AgentInstance -Name '-client' } | Should Throw
        }
        It 'rejette un nom de plus de 32 caractères' {
            { Resolve-AgentInstance -Name ('a' * 33) } | Should Throw
        }
        It 'rejette un nom réservé (logs)' {
            { Resolve-AgentInstance -Name 'logs' } | Should Throw
        }
        It 'rejette un périphérique réservé Windows (CON)' {
            { Resolve-AgentInstance -Name 'CON' } | Should Throw
        }
    }
}

Describe 'Get-PeMachineType' {
    # Fixtures : binaires natifs SQLite déjà présents dans la sortie de build de l'agent (x86 et x64).
    $x86Native = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\agent') -Recurse -Filter 'SQLite.Interop.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x86$' } | Select-Object -First 1
    $x64Native = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\agent') -Recurse -Filter 'SQLite.Interop.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } | Select-Object -First 1

    It 'identifie un binaire natif 32 bits comme x86' {
        if (-not $x86Native) { throw 'Fixture absente : aucun SQLite.Interop.dll x86 dans la sortie de build agent (construire la solution agent d''abord).' }
        Get-PeMachineType -Path $x86Native.FullName | Should Be 'x86'
    }

    It 'identifie un binaire natif 64 bits comme x64' {
        if (-not $x64Native) { throw 'Fixture absente : aucun SQLite.Interop.dll x64 dans la sortie de build agent (construire la solution agent d''abord).' }
        Get-PeMachineType -Path $x64Native.FullName | Should Be 'x64'
    }

    It 'lève si le binaire est introuvable' {
        { Get-PeMachineType -Path 'Z:\inexistant.dll' } | Should Throw
    }
}

Describe 'Pré-configuration : transport chiffré de la clé API' {

    It 'round-trip : déchiffre avec le bon mot de passe' {
        $secret = Protect-AgentPreConfigSecret -PlainText 'CLE-API-SECRETE-123' -Password 'mot-de-passe-otp' -Iterations 10000
        $clear = Unprotect-AgentPreConfigSecret -Secret $secret -Password 'mot-de-passe-otp'
        $clear | Should Be 'CLE-API-SECRETE-123'
    }

    It 'ne stocke jamais la clé en clair' {
        $secret = Protect-AgentPreConfigSecret -PlainText 'CLE-API-SECRETE-123' -Password 'otp' -Iterations 10000
        ($secret | ConvertTo-Json) | Should Not Match 'CLE-API-SECRETE-123'
    }

    It 'rejette un mot de passe erroné (échec d''intégrité MAC)' {
        $secret = Protect-AgentPreConfigSecret -PlainText 'CLE-API' -Password 'bon-otp' -Iterations 10000
        { Unprotect-AgentPreConfigSecret -Secret $secret -Password 'mauvais-otp' } | Should Throw
    }

    It 'rejette un chiffré altéré' {
        $secret = Protect-AgentPreConfigSecret -PlainText 'CLE-API' -Password 'otp' -Iterations 10000
        $tampered = $secret.PSObject.Copy()
        # Inverser un octet du chiffré.
        $bytes = [System.Convert]::FromBase64String($secret.ciphertext)
        $bytes[0] = $bytes[0] -bxor 0xFF
        $tampered.ciphertext = [System.Convert]::ToBase64String($bytes)
        { Unprotect-AgentPreConfigSecret -Secret $tampered -Password 'otp' } | Should Throw
    }
}

Describe 'New-AgentOneTimePassword' {
    It 'produit 5 groupes de 5 caractères sans symboles ambigus' {
        New-AgentOneTimePassword | Should Match '^[2-9A-HJ-NP-Z]{5}(-[2-9A-HJ-NP-Z]{5}){4}$'
    }
    It 'produit des valeurs distinctes' {
        (New-AgentOneTimePassword) | Should Not Be (New-AgentOneTimePassword)
    }
}

Describe 'Compare-ConstantTime' {
    It 'rend vrai pour deux tableaux identiques' {
        Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2, 3)) | Should Be $true
    }
    It 'rend faux pour deux tableaux différents' {
        Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2, 4)) | Should Be $false
    }
    It 'rend faux pour des longueurs différentes' {
        Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2)) | Should Be $false
    }
}
