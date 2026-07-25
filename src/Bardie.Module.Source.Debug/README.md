# Bardie.Module.Source.Debug

**Debug / test-only** fixtures for source modules — synthetic `sine` PCM so you can exercise
`StartTrack` → session FIFO → Stop/Pause without YouTube, files, or streams.

**Package id:** `Bardie.Module.Source.Debug` · **Version:** `0.1.0` · **TFM:** `net10.0`

Depends on [`Bardie.Module.Source`](../Bardie.Module.Source/README.md).

## Consume (Debug builds only)

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <ProjectReference Include="...\Bardie.Module.Source.Debug\Bardie.Module.Source.Debug.csproj" />
  <!-- or PackageReference Include="Bardie.Module.Source.Debug" -->
</ItemGroup>
```

```csharp
#if DEBUG
using Bardie.Module.Source.Debug;

builder.Services.AddSourceModuleDevFixtures(builder.Configuration);
#endif
```

| Type | Role |
|------|------|
| `DevProofTrack` | Shared `sine` / `{slug}:sine` identity + `Matches` |
| `SinePcmOptions` / `SinePcmGenerator` | Canonical s16le / 48 kHz / stereo sine stream |
| `AddSourceModuleDevFixtures` | DI registration |

Do **not** reference this package from Release / production images.

Pack: `dotnet pack libs/Bardie.Module.Source.Debug/Bardie.Module.Source.Debug.csproj -c Release`
