using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using NLog;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Test.Common;
using Radarr.Http.ClientSchema;

namespace NzbDrone.Integration.Test
{
    [Parallelizable(ParallelScope.Fixtures)]
    public abstract class IntegrationTest : IntegrationTestBase
    {
        protected static int StaticPort = 7878;

        protected NzbDroneRunner _runner;

        public override string MovieRootFolder => GetTempDirectory("MovieRootFolder");

        protected int Port { get; private set; }

        protected PostgresOptions PostgresOptions { get; set; } = new ();

        protected override string RootUrl => $"http://localhost:{Port}/";

        protected override string ApiKey => _runner.ApiKey;

        protected override void StartTestTarget()
        {
            Port = Interlocked.Increment(ref StaticPort);

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables("Radarr__")
                .Build();

            config.GetSection("Postgres").Bind(PostgresOptions);

            if (PostgresOptions.Host != null)
            {
                var uid = TestBase.GetUID();
                PostgresOptions.MainDb = uid + "_main";
                PostgresOptions.LogDb = uid + "_log";
                CreatePostgresDb(PostgresOptions);
            }

            _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), PostgresOptions, Port);
            _runner.Kill();

            _runner.Start();
        }

        protected override void InitializeTestTarget()
        {
            // Make sure tasks have been initialized so the config put below doesn't cause errors
            WaitForCompletion(() => Tasks.All().SelectList(x => x.TaskName).Contains("RssSync"));

            Indexers.Post(new Radarr.Api.V3.Indexers.IndexerResource
            {
                EnableRss = false,
                EnableInteractiveSearch = false,
                EnableAutomaticSearch = false,
                ConfigContract = nameof(NewznabSettings),
                Implementation = nameof(Newznab),
                Name = "NewznabTest",
                Protocol = Core.Indexers.DownloadProtocol.Usenet,
                Fields = SchemaBuilder.ToSchema(new NewznabSettings())
            });

            // Change Console Log Level to Debug so we get more details.
            var config = HostConfig.Get(1);
            config.ConsoleLogLevel = "Debug";
            HostConfig.Put(config);
        }

        protected override void StopTestTarget()
        {
            _runner.Kill();
            if (PostgresOptions.Host != null)
            {
                DropPostgresDb(PostgresOptions);
            }
        }

        private void CreatePostgresDb(PostgresOptions options)
        {
            var connectionString = GetConnectionString(options);
            CreateDatabase(connectionString, options.User, options.MainDb);
            CreateDatabase(connectionString, options.User, options.LogDb);
        }

        private void CreateDatabase(string connectionString, string user, string db)
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{db}\" WITH OWNER = {user} ENCODING = 'UTF8' CONNECTION LIMIT = -1;";
            cmd.ExecuteNonQuery();
        }

        private void DropPostgresDb(PostgresOptions options)
        {
            var connectionString = GetConnectionString(options);
            DropDatabase(connectionString, options.MainDb);
            DropDatabase(connectionString, options.LogDb);
        }

        private void DropDatabase(string connectionString, string db)
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE \"{db}\";";
            cmd.ExecuteNonQuery();
        }

        private string GetConnectionString(PostgresOptions options)
        {
            var builder = new NpgsqlConnectionStringBuilder()
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password
            };

            return builder.ConnectionString;
        }
    }
}
