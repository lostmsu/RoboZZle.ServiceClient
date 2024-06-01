namespace RoboZZle.WebService;

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents RoboZZle web service async wrapper
/// </summary>
public sealed class RoboZZleService {
    readonly IRobozzleService robozzleService;
    readonly Dictionary<int, Puzzle> memoryCache = new(8000);
    readonly Dictionary<int, LevelInfo2> levelCache = new(8000);
    readonly PuzzleList campaign;
    readonly LevelCache serviceLevelCache;

    const int BLOCK_SIZE = 32;

    /// <summary>
    /// Create an instance of web service async wrapper
    /// </summary>
    /// <param name="levelCache">Local cache</param>
    /// <param name="robozzleService">Optional client instance. If not supplied, a default one is used.</param>
    public RoboZZleService(LevelCache levelCache,
                           RobozzleServiceClient? robozzleService = null) {
        this.serviceLevelCache =
            levelCache ?? throw new ArgumentNullException(nameof(levelCache));
        this.robozzleService = robozzleService ?? new RobozzleServiceClient();
        this.campaign = new PuzzleList(PuzzleList.CAMPAIGN, this.QueryCampaign,
                                       this.serviceLevelCache);
    }

    public string? UserName { get; set; }
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Loads all puzzles from the local cache into memory
    /// </summary>
    public async Task InitializeCache(CancellationToken cancel = default) {
        if (this.levelCache.Count > 0)
            return;

        Exception? exception = null;
        try {
            await this.serviceLevelCache.Load(this.levelCache, cancel).ConfigureAwait(false);
        } catch (SerializationException e) {
            exception = e;
            // TODO: toast
        }

        if (exception != null)
            await this.serviceLevelCache.Save(this.levelCache).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to log in
    /// </summary>
    public async Task<Dictionary<int, int>?> Login(string userName, string passwordHash) {
        DebugEx.WriteLine("logging in");
        var logIn2Request = new LogIn2Request(userName, passwordHash);
        var result = await this.robozzleService.LogIn2Async(logIn2Request)
                               .ConfigureAwait(false);
        DebugEx.WriteLine("logged in");
        if (!result.LogIn2Result) {
            return null;
        } else {
            this.UserName = userName;
            this.PasswordHash = passwordHash;
            return result.solvedLevels;
        }
    }

    /// <summary>
    /// Attempts to register new user
    /// </summary>
    /// <param name="userName">New user name</param>
    /// <param name="passwordHash">New user password hash</param>
    /// <param name="email">User's email address</param>
    /// <param name="solved">List of solved puzzles</param>
    public async Task<string?> Register(string userName, string passwordHash, string email,
                                        int[] solved) {
        if (userName == null) throw new ArgumentNullException(nameof(userName));
        if (passwordHash == null) throw new ArgumentNullException(nameof(passwordHash));
        if (email == null) throw new ArgumentNullException(nameof(email));

        string? result = await this.robozzleService
                                   .RegisterUserAsync(userName, passwordHash, email, solved)
                                   .ConfigureAwait(false);
        this.UserName = userName;
        this.PasswordHash = passwordHash;
        return result;
    }

    /// <summary>
    /// Attempts to retrieve new puzzles from RoboZZle web service.
    /// To avoid excessive bandwidth usage, initialize cache first.
    /// </summary>
    /// <returns>IDs of new puzzles</returns>
    public async Task<int[]> QueryNewPuzzles(IProgress<double> progress) {
        // TODO: lock
        var result = new List<int>();
        DebugEx.WriteLine("querying for new puzzles");
        if (this.levelCache.Count == 0) {
            await this.RetrievePuzzlesBlob().ConfigureAwait(false);
            result.AddRange(this.levelCache.Keys);
            DebugEx.WriteLine("puzzles blob retrieved");
        }

        int maxLevelID = this.GetMaxLevelID();

        int newestLevel = 0;
        int toDownload = 1;

        for (int blockIndex = 0;; blockIndex++) {
            LevelInfo2[] newLevels = await this
                                           .GetLevelsPaged(
                                               blockIndex, BLOCK_SIZE, SortKind.DESCENDING_ID,
                                               null).ConfigureAwait(false);

            if (newLevels.Length == 0)
                break;

            if (newestLevel == 0) {
                newestLevel = newLevels[0].Id;
                toDownload = Math.Max(1, newestLevel - maxLevelID);
            }

            int downloaded = newestLevel - newLevels[0].Id;
            double progressValue = downloaded * 1.0 / toDownload;
            progress?.Report(progressValue);

            bool found = false;
            lock (this.levelCache) {
                foreach (var level in newLevels) {
                    if (level.Id <= maxLevelID) {
                        found = true;
                        break;
                    }

                    this.levelCache[level.Id] = level;

                    if (level.Id > maxLevelID)
                        result.Add(level.Id);
                }
            }

            if (found)
                break;
        }

        result.Sort();

        DebugEx.WriteLine("{0} new puzzles retrieved", result.Count);

        return result.ToArray();
    }

    const string LEVELS_BLOB_URI = "https://robcdn.blob.core.windows.net/rob/levels.zip";

    async Task RetrievePuzzlesBlob() {
        var httpClient = new HttpClient();
        try {
            using var puzzlesZipStream =
                await httpClient.GetStreamAsync(LEVELS_BLOB_URI).ConfigureAwait(false);
            Debug.WriteLine("started downloading levels blob");

            var zipArchive = new ZipArchive(puzzlesZipStream, ZipArchiveMode.Read);
            using var levelsXmlBinary = zipArchive.GetEntry("levels.xml")!.Open();
            await this.serviceLevelCache.Save(levelsXmlBinary).ConfigureAwait(false);

            Debug.WriteLine("levels blob downloaded");
        } catch (Exception error) {
            Debug.WriteLine($"failed to download levels blob: {error}");
        }

        await this.InitializeCache().ConfigureAwait(false);
    }

    /// <summary>
    /// Save all known puzzles into local cache
    /// </summary>
    public Task SaveCache() {
        return this.serviceLevelCache.Save(this.levelCache);
    }

    /// <summary>
    /// Clears local puzzle cache
    /// </summary>
    public async Task ClearCache() {
        lock (this.memoryCache) {
            this.memoryCache.Clear();
        }

        this.levelCache.Clear();
        await this.serviceLevelCache.Clear().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets puzzle from local cache by its ID
    /// </summary>
    /// <param name="puzzleID">Puzzle ID</param>
    /// <returns>Puzzle</returns>
    public Puzzle? GetPuzzle(int puzzleID) {
        Puzzle puzzle;
        lock (this.memoryCache) {
            if (this.memoryCache.TryGetValue(puzzleID, out puzzle))
                return puzzle;
        }

        if (!this.levelCache.TryGetValue(puzzleID, out var level) || level == null)
            return null;

        puzzle = level.ToPuzzle();
        lock (this.memoryCache) {
            this.memoryCache[puzzleID] = puzzle;
        }

        return puzzle;
    }

    /// <summary>
    /// Gets puzzle IDs of all locally stored puzzles
    /// </summary>
    /// <returns>Puzzle IDs</returns>
    public int[] GetLocalPuzzleIDs() {
        lock (this.levelCache) {
            return this.levelCache.Keys.ToArray();
        }
    }

    public Task<int[]?> GetCampaignPuzzleIDs() => this.campaign.GetValue();

    /// <summary>
    /// Submits puzzle solution
    /// </summary>
    /// <param name="puzzleID">ID of the puzzle to submit solution for</param>
    /// <param name="solution">Solution program</param>
    /// <returns>Submission task</returns>
    public async Task SubmitSolution(int puzzleID, Program solution) {
        if (solution == null) throw new ArgumentNullException(nameof(solution));

        DebugEx.WriteLine("submitting solution for {0}", puzzleID);
        string serializedSolution = solution.Encode(true);
        string? result = await this.robozzleService
                                   .SubmitSolutionAsync(puzzleID, this.UserName,
                                                        this.PasswordHash, serializedSolution)
                                   .ConfigureAwait(false);
        if (result == null)
            return;

        var error = new ServiceResponseException(result);
        DebugEx.WriteLine("FAILED: submit solution for {0}: {1}", puzzleID, error.Message);
        throw error;
    }

    async Task<int[]> QueryCampaign() {
        var result = new List<int>();
        LevelInfo2[] levels;
        int block = 0;
        do {
            levels = await this.GetLevelsPaged(block, 20, SortKind.CAMPAIGN, null)
                               .ConfigureAwait(false);
            result.AddRange(levels.Select(level => level.Id));
            block++;
        } while (levels.Length > 0);

        return result.ToArray();
    }

    async Task<LevelInfo2[]> GetLevelsPaged(int blockIndex, int blockLength, SortKind sort,
                                            string? excludeSolvedByUser) {
        var request =
            new GetLevelsPagedRequest(blockIndex, blockLength, (int)sort, excludeSolvedByUser);
        var levels = await this.robozzleService.GetLevelsPagedAsync(request)
                               .ConfigureAwait(false);
        return levels.GetLevelsPagedResult;
    }

    int GetMaxLevelID() {
        int[] cached = this.GetLocalPuzzleIDs();
        return cached.Length == 0 ? -1 : cached.Max();
    }
}