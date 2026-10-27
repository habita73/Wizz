Start-Process dotnet -ArgumentList "run --project \"X:\source\Habita73\Vent-au-comptoir\src\Wizz.Presentation\Wizz.Presentation\Wizz.Presentation.csproj\" --configuration Release --no-build --urls http://localhost:5183" -WindowStyle Hidden
Start-Sleep -Seconds 5
Start-Process "http://127.0.0.1:5183"
