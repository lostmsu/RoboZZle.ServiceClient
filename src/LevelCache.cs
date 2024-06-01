namespace RoboZZle.WebService;

using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using PCLStorage;

using FileAccess = PCLStorage.FileAccess;

public sealed class LevelCache {
    public LevelCache(IFolder cacheFolder) {
        this.cacheFolder = cacheFolder ?? throw new ArgumentNullException(nameof(cacheFolder));
    }

    #region Fields

    const string LEVELS_FILE_NAME = "levels.xml";
    public const string CAMPAIGN_FILE_NAME = "campaign.txt";

    readonly IFolder cacheFolder;
    readonly DataContractSerializer serializer = new(typeof(LevelInfo2[]));
    readonly XmlWriterSettings xmlWriterSettings = new() { Async = true };

    #endregion

    public async Task Load(IDictionary<int, LevelInfo2> levels,
                           CancellationToken cancel = default) {
        IFile levelCache;
        try {
            levelCache =
                await this.cacheFolder.GetFileAsync(LEVELS_FILE_NAME, cancel).ConfigureAwait(false);
        } catch (FileNotFoundException) {
            return;
        }

        if (levelCache == null)
            return;

        await this.LoadFromFile(levels, levelCache, cancel).ConfigureAwait(false);
    }

    async Task LoadFromFile(IDictionary<int, LevelInfo2> levels, IFile levelCache,
                            CancellationToken cancel) {
        using Stream stream = await levelCache.OpenAsync(FileAccess.Read, cancel)
                                              .ConfigureAwait(false);
        using XmlReader reader = XmlReader.Create(stream);
        var cachedLevels = (LevelInfo2[])this.serializer.ReadObject(reader);

        foreach (var level in cachedLevels)
            levels[level.Id] = level;
    }

    public async Task Save(IDictionary<int, LevelInfo2> levels) {
        IFile levelCache = await this.cacheFolder
                                     .CreateFileAsync(LEVELS_FILE_NAME,
                                                      CreationCollisionOption.ReplaceExisting)
                                     .ConfigureAwait(false);

        using Stream stream =
            await levelCache.OpenAsync(FileAccess.ReadAndWrite).ConfigureAwait(false);
        using XmlWriter writer = XmlWriter.Create(stream, this.xmlWriterSettings);
        var levelsToCache = levels.Values.ToArray();
        this.serializer.WriteObject(writer, levelsToCache);
        await writer.FlushAsync().ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public async Task Save(Stream data) {
        IFile levelCache = await this.cacheFolder
                                     .CreateFileAsync(LEVELS_FILE_NAME,
                                                      CreationCollisionOption.ReplaceExisting)
                                     .ConfigureAwait(false);

        using Stream stream =
            await levelCache.OpenAsync(FileAccess.ReadAndWrite).ConfigureAwait(false);
        await data.CopyToAsync(stream).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public async Task<int[]?> LoadPuzzleList(string listFileName,
                                             CancellationToken cancel = default) {
        IFile list;
        try {
            list = await this.cacheFolder.GetFileAsync(listFileName, cancel).ConfigureAwait(false);
        } catch (FileNotFoundException) {
            return null;
        }

        if (list == null)
            return null;

        string puzzles = await list.ReadAllTextAsync().ConfigureAwait(false);
        return puzzles.Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
                      .Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray();
    }

    public async Task SavePuzzleList(int[] levelIDs, string listFileName) {
        IFile list = await this.cacheFolder
                               .CreateFileAsync(listFileName,
                                                CreationCollisionOption.ReplaceExisting)
                               .ConfigureAwait(false);
        string puzzles =
            string.Join("\n", levelIDs.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        await list.WriteAllTextAsync(puzzles).ConfigureAwait(false);
    }

    public async Task Clear() {
        var files = await this.cacheFolder.GetFilesAsync().ConfigureAwait(false);
        await Task.WhenAll(files.Select(file => file.DeleteAsync())).ConfigureAwait(false);
    }
}