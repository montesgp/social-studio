using System.Text;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;
using SocialStudio.Infrastructure.Social.Discovery;
using SocialStudio.Tests.Common;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SocialStudio.Tests.Infrastructure.Social.Discovery;

/// <summary>
/// The LOCAL seed importer loads captured Apify discovery output into the candidate bank so the
/// shortlist and the AI Analysis page have something real to work with without buying another run.
///
/// The behaviours asserted here are the ones that make it safe to leave wired at startup: it maps
/// through the PRODUCTION adapter (so a mapping regression surfaces as a failed import rather than
/// silently good-looking seed data), it never duplicates on restart, one malformed file never costs
/// the others, it refuses to guess a tenant, and it does nothing at all unless it is both enabled
/// and running in Development.
/// </summary>
public sealed class DiscoverySeedImporterTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _seedDirectory =
        Path.Combine(Path.GetTempPath(), "hive-discovery-seed-" + Guid.NewGuid().ToString("N"));

    public DiscoverySeedImporterTests() => Directory.CreateDirectory(_seedDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_seedDirectory))
        {
            Directory.Delete(_seedDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Imports_FlattenedApifyItems_ThroughTheProductionAdapter()
    {
        var tenant = NewTenant("Solo org");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var importer = NewImporter(bank, new InMemoryTenantRepository(tenant));

        await importer.ImportAsync();

        var banked = Assert.Single(bank.Observations(tenant.Id.Value, "tiktok"));

        Assert.Equal("niche", banked.SourceKey);
        Assert.Equal("tiktok", banked.NetworkKey);
        // SourceHandle comes from the FLATTENED "authorMeta.name" property, not a nested object.
        Assert.Equal("sofi_detesting14", banked.AuthorHandle);
        Assert.Equal("https://www.tiktok.com/@sofi_detesting14/video/7523521255069191429", banked.ContentUrl);
        Assert.Equal(664700, banked.Views);
        Assert.Equal(51300, banked.Likes);
        Assert.Equal(892, banked.Comments);
        Assert.Equal(1694, banked.Shares);
        Assert.Equal(
            new DateTimeOffset(2025, 7, 5, 9, 5, 5, TimeSpan.Zero),
            banked.PublishedAtUtc);
        Assert.Equal(
            CandidateIdentity.From("tiktok", "https://www.tiktok.com/@sofi_detesting14/video/7523521255069191429"),
            banked.CandidateKey);

        // Banked under exactly the envelope's hashtags — the same key a live run would have used.
        Assert.Equal(["software", "technology"], bank.HashtagsFor(tenant.Id.Value, banked.CandidateKey).Order());
    }

    [Fact]
    public async Task ReImporting_DoesNotDuplicate()
    {
        var tenant = NewTenant("Solo org");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var tenants = new InMemoryTenantRepository(tenant);

        await NewImporter(bank, tenants).ImportAsync();
        await NewImporter(bank, tenants).ImportAsync();

        Assert.Single(bank.Observations(tenant.Id.Value, "tiktok"));
    }

    [Fact]
    public async Task MalformedFile_IsSkipped_AndTheOtherFilesStillImport()
    {
        var tenant = NewTenant("Solo org");
        WriteSeedFile("aaa-broken.json", "{ this is not json");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();

        await NewImporter(bank, new InMemoryTenantRepository(tenant)).ImportAsync();

        Assert.Single(bank.Observations(tenant.Id.Value, "tiktok"));
    }

    [Fact]
    public async Task ImportsNothing_WhenSeveralOrganizationsAreActiveAndNoneIsConfigured()
    {
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var tenants = new InMemoryTenantRepository(NewTenant("Org A"), NewTenant("Org B"));

        await NewImporter(bank, tenants).ImportAsync();

        Assert.Equal(0, bank.RecordCallCount);
    }

    [Fact]
    public async Task ImportsNothing_WhenNoOrganizationIsActiveAndNoneIsConfigured()
    {
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();

        await NewImporter(bank, new InMemoryTenantRepository()).ImportAsync();

        Assert.Equal(0, bank.RecordCallCount);
    }

    [Fact]
    public async Task UsesTheConfiguredOrganization_EvenWhenSeveralAreActive()
    {
        var target = NewTenant("Org A");
        var other = NewTenant("Org B");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var importer = NewImporter(
            bank,
            new InMemoryTenantRepository(target, other),
            configure: options => options.OrganizationId = target.Id.Value);

        await importer.ImportAsync();

        Assert.Single(bank.Observations(target.Id.Value, "tiktok"));
        Assert.Empty(bank.Observations(other.Id.Value, "tiktok"));
    }

    [Fact]
    public async Task DoesNothing_WhenDisabledByConfiguration()
    {
        var tenant = NewTenant("Solo org");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var importer = NewImporter(
            bank,
            new InMemoryTenantRepository(tenant),
            configure: options => options.Enabled = false);

        await importer.ImportAsync();

        Assert.Equal(0, bank.RecordCallCount);
    }

    [Fact]
    public async Task DoesNothing_OutsideDevelopment_EvenWhenEnabled()
    {
        var tenant = NewTenant("Solo org");
        WriteSeedFile("tiktok-software.json", SoftwareEnvelope);

        var bank = new InMemoryNicheBank();
        var importer = NewImporter(
            bank,
            new InMemoryTenantRepository(tenant),
            environmentName: Environments.Production);

        await importer.ImportAsync();

        Assert.Equal(0, bank.RecordCallCount);
    }

    /// <summary>
    /// The COMMITTED seed files, through the production adapter, into the bank.
    ///
    /// Every other test here drives a hand-written envelope, which proves the importer works but says
    /// nothing about the files a developer actually gets. Since the seed is enabled by default in
    /// Development, those files are the difference between a local environment with data and one that
    /// silently starts empty — and an adapter change that stops mapping them would otherwise surface as
    /// "the AI Analysis page shows nothing" days later, with no obvious cause.
    /// </summary>
    [Fact]
    public async Task TheCommittedSeedFiles_MapThroughTheProductionAdapter_IntoTheBank()
    {
        var seedDirectory = Path.Combine(
            FindRepositoryRoot(),
            "SocialStudio.ApiService",
            "seed-data",
            "discovery");

        Assert.True(
            Directory.Exists(seedDirectory),
            $"The committed discovery seed directory is missing: {seedDirectory}");

        var target = NewTenant("Target org");
        var bank = new InMemoryNicheBank();

        var importer = NewImporter(
            bank,
            new InMemoryTenantRepository(target, NewTenant("Other org")),
            configure: options =>
            {
                options.DirectoryPath = seedDirectory;
                options.OrganizationId = target.Id.Value;
            });

        await importer.ImportAsync();

        var banked = bank.Observations(target.Id.Value, "tiktok");
        Assert.NotEmpty(banked);

        // Every banked row must be usable by the tools that read the bank: a candidate with no URL
        // cannot be opened, and one with no handle cannot be cited.
        Assert.All(banked, observation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(observation.ContentUrl));
            Assert.False(string.IsNullOrWhiteSpace(observation.AuthorHandle));
            Assert.Equal("niche", observation.SourceKey);
        });

        // Filed under the hashtags the envelopes declare, so a niche search can actually serve from it.
        Assert.All(banked, observation =>
            Assert.NotEmpty(bank.HashtagsFor(target.Id.Value, observation.CandidateKey)));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SocialStudio.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>One verbatim item in the flattened dot-notation shape the real actor returns.</summary>
    private const string SoftwareEnvelope = """
    {
      "networkKey": "tiktok",
      "hashtags": ["software", "technology"],
      "sourceNote": "Captured run output.",
      "items": [
        {
          "authorMeta.name": "sofi_detesting14",
          "text": "#fyp #software\nsecond line",
          "diggCount": 51300,
          "shareCount": 1694,
          "playCount": 664700,
          "commentCount": 892,
          "videoMeta.duration": 8,
          "musicMeta.musicName": "original sound",
          "musicMeta.musicAuthor": "V",
          "createTimeISO": "2025-07-05T09:05:05.000Z",
          "webVideoUrl": "https://www.tiktok.com/@sofi_detesting14/video/7523521255069191429"
        }
      ]
    }
    """;

    private void WriteSeedFile(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_seedDirectory, fileName), content, new UTF8Encoding(false));

    private DiscoverySeedImporter NewImporter(
        IContentCandidateRepository bank,
        ITenantRepository tenants,
        Action<DiscoverySeedImportOptions>? configure = null,
        string? environmentName = null)
    {
        var options = new DiscoverySeedImportOptions
        {
            Enabled = true,
            DirectoryPath = _seedDirectory,
        };

        configure?.Invoke(options);

        return new DiscoverySeedImporter(
            Options.Create(options),
            new TestHostEnvironment(environmentName ?? Environments.Development, _seedDirectory),
            tenants,
            bank,
            new TestClock(Now));
    }

    private static Tenant NewTenant(string name) =>
        Tenant.Create(new TenantId(Guid.NewGuid()), name, Guid.NewGuid(), Now).Value;

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName, string contentRootPath)
        {
            EnvironmentName = environmentName;
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "SocialStudio.Tests";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Bank double that reproduces the ONE behaviour the importer's idempotency rests on: dedup by
    /// <see cref="CandidateIdentity"/> within an organization + network, widening the hashtag set
    /// instead of storing the candidate again.
    /// </summary>
    private sealed class InMemoryNicheBank : IContentCandidateRepository
    {
        private readonly Dictionary<(Guid, string, string), CandidateObservation> _banked = [];
        private readonly Dictionary<(Guid, string), HashSet<string>> _hashtags = [];

        public int RecordCallCount { get; private set; }

        public IReadOnlyList<CandidateObservation> Observations(Guid organizationId, string networkKey) =>
            _banked
                .Where(entry => entry.Key.Item1 == organizationId && entry.Key.Item2 == networkKey)
                .Select(entry => entry.Value)
                .ToList();

        public IReadOnlyList<string> HashtagsFor(Guid organizationId, string candidateKey) =>
            _hashtags.TryGetValue((organizationId, candidateKey), out var tags) ? [.. tags] : [];

        public Task<int> RecordNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            IReadOnlyList<CandidateObservation> observations,
            CancellationToken ct = default)
        {
            RecordCallCount++;

            var newCount = 0;
            foreach (var observation in observations)
            {
                var key = (organizationId, networkKey, observation.CandidateKey);
                if (_banked.TryAdd(key, observation))
                {
                    newCount++;
                }

                if (!_hashtags.TryGetValue((organizationId, observation.CandidateKey), out var tags))
                {
                    tags = [];
                    _hashtags[(organizationId, observation.CandidateKey)] = tags;
                }

                foreach (var hashtag in hashtags)
                {
                    tags.Add(hashtag);
                }
            }

            return Task.FromResult(newCount);
        }

        public Task RecordObservationsAsync(
            Guid organizationId,
            Guid clientId,
            IReadOnlyList<CandidateObservation> observations,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CandidateObservation>> GetLatestByKeysAsync(
            Guid organizationId,
            Guid clientId,
            IReadOnlyList<string> candidateKeys,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CandidateObservation>>([]);

        public Task<IReadOnlyList<CandidateObservation>> GetNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            int maxResults,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CandidateObservation>>(
                Observations(organizationId, networkKey).Take(maxResults).ToList());
    }

    private sealed class InMemoryTenantRepository : ITenantRepository
    {
        private readonly List<Tenant> _tenants;

        public InMemoryTenantRepository(params Tenant[] tenants) => _tenants = [.. tenants];

        public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.FirstOrDefault(x => x.Id == id));

        public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
            TenantQuery query,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Tenant> source = _tenants;
            if (!query.IncludeInactive)
            {
                source = source.Where(x => x.IsActive);
            }

            var items = source.ToList();
            return Task.FromResult<(IReadOnlyList<Tenant>, int)>((items, items.Count));
        }

        public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            _tenants.Add(tenant);
            return Task.CompletedTask;
        }

        public void Update(Tenant tenant)
        {
        }
    }
}
