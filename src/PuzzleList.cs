namespace RoboZZle.WebService;

using System.Threading;
using System.Threading.Tasks;

sealed class PuzzleList {
    public PuzzleList(string name, Func<Task<int[]>> retriever, LevelCache levelCache) {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        this.levelCache = levelCache ?? throw new ArgumentNullException(nameof(levelCache));
        this.retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
        this.fileName = name + ".lst";
    }

    readonly LevelCache levelCache;
    readonly Func<Task<int[]>> retriever;
    readonly string fileName;
    volatile int[]? list;

    public async Task<int[]?> GetValue(CancellationToken cancel = default) {
        int[]? presentList = this.list;
        if (presentList != null)
            return presentList;

        presentList = await this.levelCache.LoadPuzzleList(this.fileName, cancel)
                                .ConfigureAwait(false);
        if (presentList != null) {
            this.list = presentList;
            return presentList;
        }

        presentList = await this.retriever().ConfigureAwait(false);
        if (presentList != null) {
            this.list = presentList;
            await this.levelCache.SavePuzzleList(presentList, this.fileName).ConfigureAwait(false);
        }

        return presentList;
    }

    public const string CAMPAIGN = "campaign";
}