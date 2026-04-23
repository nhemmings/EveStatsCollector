using DbUp;

var connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? args.FirstOrDefault()
    ?? throw new InvalidOperationException(
        "Provide a connection string via the ConnectionStrings__Postgres env var or as the first argument.");

EnsureDatabase.For.PostgresqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
return result.Successful ? 0 : 1;
