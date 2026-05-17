param(
    [string]$OutputDirectory = "artifacts/devkitpro"
)

$ErrorActionPreference = "Stop"

$examples = @(
    @{ Root = "fixtures\devkitpro"; Example = "gx-ladder"; Description = "ngcSharp deterministic GX ladder" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\triangle"; Description = "libogc indexed color triangle" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\texturetest"; Description = "libogc textured quad via TPL" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\gxSprites"; Description = "libogc sprite/texture example" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\acube"; Description = "libogc rotating cube" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson01"; Description = "NeHe GX lesson 01" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson02"; Description = "NeHe GX lesson 02" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson03"; Description = "NeHe GX lesson 03" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson04"; Description = "NeHe GX lesson 04" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson05"; Description = "NeHe GX lesson 05" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson06"; Description = "NeHe GX lesson 06 textured cube" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson07"; Description = "NeHe GX lesson 07 texture/filtering" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson08"; Description = "NeHe GX lesson 08 blending" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson09"; Description = "NeHe GX lesson 09 textured particles" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson10"; Description = "NeHe GX lesson 10 world/textured geometry" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson11"; Description = "NeHe GX lesson 11 wave texture" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson12"; Description = "NeHe GX lesson 12 display lists/textures" },
    @{ Root = "C:\devkitPro\examples\gamecube"; Example = "graphics\gx\neheGX\lesson19"; Description = "NeHe GX lesson 19 particles" }
)

foreach ($entry in $examples) {
    Write-Host "==== Building $($entry.Example): $($entry.Description) ===="
    & "$PSScriptRoot\build-devkitpro-example.ps1" `
        -ExamplesRoot $entry.Root `
        -Example $entry.Example `
        -OutputDirectory $OutputDirectory
}
