var builder = DistributedApplication.CreateBuilder(args);

// the database: an existing SQL Server when ConnectionStrings:starlights-db is configured (e.g. in this project's
// user secrets), otherwise SQL Server in a container. The migration workers create the database if it is missing.
var useExternalDatabase = !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:starlights-db"]);
IResourceBuilder<IResourceWithConnectionString> database = useExternalDatabase
    ? builder.AddConnectionString("starlights-db")
    : builder.AddSqlServer("sql-server", port: 61070) // static port for now, easier to view database in SSMS
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume()
        .AddDatabase("starlights-db", "starlights-db-env");

// the admin key (content administration) and the master password (the app's key button); stored in this project's
// user secrets as Parameters:admin-key and Parameters:master-password, the dashboard asks for them when missing
var adminKey = builder.AddParameter("admin-key", secret: true);
var masterPassword = builder.AddParameter("master-password", secret: true);

// local folders for what the Docker install keeps under /data, in .devdata/ (git-ignored): the downloaded content,
// homebrew, portraits and the Compendium of Lore (served at /lore-data by the builder's Vite dev server, LORE_DIR)
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
var devData = Path.Combine(repositoryRoot, ".devdata");
var loreData = Path.Combine(devData, "lore");
var builderApp = Path.Combine(repositoryRoot, "src", "frontend", "apps", "builder-app");

var application = builder.AddProject<Projects.Starlights_Application>("backend")
    .WithUrls(context => context.Urls.ForEach(url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly))
    .WithScalarCommand()
    .WithScalarUrl()
    .WithInitializeDatabaseCommand()
    .WithReference(database)
    .WithEnvironment("Admin__Key", adminKey)
    .WithEnvironment("Admin__MasterPassword", masterPassword)
    // the app brings its own content in from the links on its Content page (Aurora Legacy, 5etools)
    .WithEnvironment("Elements__SampleContent", "false")
    .WithEnvironment("Content__SelfManaged", "true")
    .WithEnvironment("Content__Folder", Path.Combine(devData, "content"))
    .WithEnvironment("Content__FiveEToolsSource", Path.Combine(devData, "content", "5etools"))
    .WithEnvironment("FiveETools__Path", Path.Combine(devData, "content", "5etools", "data"))
    .WithEnvironment("Aurora__ContentPath", Path.Combine(devData, "content", "aurora-elements"))
    .WithEnvironment("Aurora__HomebrewPath", Path.Combine(devData, "homebrew"))
    .WithEnvironment("Aurora__PullRequestsPath", Path.Combine(devData, "aurora-pull-requests"))
    .WithEnvironment("Portraits__Path", Path.Combine(devData, "portraits"))
    .WithEnvironment("Content__LorePath", loreData)
    .WithEnvironment("Content__LoreIngest", Path.Combine(builderApp, "lore-ingest", "ingest.ts"))
    .WithEnvironment("Content__Node", "node");

if (!useExternalDatabase)
{
    application.WaitFor(database);
}

if (builder.ExecutionContext.IsRunMode)
{
    var migrationResource = builder.AddMigrationsResource("migrations");

    var elements = migrationResource.WithMigrationWorker<Projects.Modules_Elements_Data_EntityFramework_MigrationService>("elements-context")
        .WithReference(database);
    if (!useExternalDatabase)
    {
        elements.WaitFor(database);
    }

    var characters = migrationResource.WithMigrationWorker<Projects.Modules_Characters_Data_EntityFramework_MigrationService>("characters-context")
        .WithReference(database)
        .WaitFor(elements);

    // wait for the migration services to complete before starting the application
    application.WaitForCompletion(elements);
    application.WaitForCompletion(characters);
}

// the web app: builder-app is the whole Starfall site (the only app the Docker install serves). The landing-page,
// content-manager and showcase apps are early Starlights scaffolds Starfall doesn't use, so they are not started
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddJavaScriptApp("app-character-builder", "../../frontend/apps/builder-app", "dev")
        //.WithEnvironment("BROWSER", "none")
        .WithEnvironment("LORE_DIR", loreData)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .WithReference(application)
        .WaitFor(application);
}

if (builder.ExecutionContext.IsRunMode)
{
    // this section allows access to the frontend from outside the local network using a developer tunnel
    // this does not start the tunnel automatically, the tunnel must be started explicitly
    // when the dev tunnel becomes available, the frontend will start automatically

    var tunnel = builder.AddDevTunnel("dev-tunnel")
        .WithAnonymousAccess()
        .WithExplicitStart();

    var app = builder.AddJavaScriptApp("dev-tunnel-react-builder-app", "../../frontend/apps/builder-app", "dev")
        .WithIconName("Cloud", IconVariant.Regular)
        .WithEnvironment("LORE_DIR", loreData)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints()
        .WithReference(application, tunnel)
        .WithParentRelationship(tunnel)
        .WaitFor(application);

    tunnel.WithReference(application);
    tunnel.WithReference(app);
}

builder.Build().Run();