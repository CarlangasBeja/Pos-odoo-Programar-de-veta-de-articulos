using Pos.Infrastructure.Security;

// Uso: dotnet run --project tools/Pos.Tools.HashPassword -- "<contraseña>"
// Copie el resultado en Operators:Accounts[n]:PasswordHash (appsettings o user-secrets).
if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/Pos.Tools.HashPassword -- \"<contraseña>\"");
    return 1;
}

Console.WriteLine(new Pbkdf2PasswordHasher().Hash(args[0]));
return 0;
