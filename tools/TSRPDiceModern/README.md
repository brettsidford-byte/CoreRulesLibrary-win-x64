# TSRPDiceModern

Modern replacement for the 1999 Core Rules dice utility. Uses `RandomNumberGenerator.GetInt32`, avoiding the original millisecond-only seed and modulo mapping. Supports d4/d6/d8/d10/d12/d20/d100, 1-100 dice, modifiers and roll history.

Build: `dotnet publish TSRPDiceModern.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
