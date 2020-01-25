using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using Proyecto26;
using RSG;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

public class LevelManager
{
    public const string CoverThumbnailFilename = ".cover";
    
    public readonly LevelLoadProgressEvent OnLevelLoadProgress = new LevelLoadProgressEvent();
    public readonly LevelEvent OnLevelMetaUpdated = new LevelEvent();
    public readonly LevelEvent OnLevelDeleted = new LevelEvent();

    public readonly Dictionary<string, Level> LoadedLocalLevels = new Dictionary<string, Level>();
    private readonly HashSet<string> loadedPaths = new HashSet<string>();

    public void DeleteLocalLevel(string id)
    {
        if (!LoadedLocalLevels.ContainsKey(id))
        {
            Debug.LogWarning($"Warning: Could not find level {id}");
            return;
        }
        var level = LoadedLocalLevels[id];
        Directory.Delete(Path.GetDirectoryName(level.Path) ?? throw new InvalidOperationException(), true);
        LoadedLocalLevels.Remove(level.Id);
        loadedPaths.Remove(level.Path);
        OnLevelDeleted.Invoke(level);
    }

    public Promise<LevelMeta> FetchLevelMeta(string levelId, bool updateLocal = false)
    {
        return new Promise<LevelMeta>((resolve, reject) =>
        {
            RestClient.Get<OnlineLevel>(new RequestHelper
            {
                Uri = $"{Context.ApiBaseUrl}/levels/{levelId}"
            }).Then(it =>
            {
                var remoteMeta = it.GenerateLevelMeta();
                if (updateLocal && LoadedLocalLevels.ContainsKey(levelId))
                {
                    var localLevel = LoadedLocalLevels[levelId];
                    if (UpdateLevelMeta(localLevel, remoteMeta))
                    {
                        Debug.Log($"Level meta updated for {localLevel.Id}");
                        OnLevelMetaUpdated.Invoke(localLevel);
                    }
                }

                resolve(remoteMeta);
            }).Catch(error =>
            {
                if (!error.IsNetworkError && error.StatusCode == 404)
                {
                    Debug.Log($"Level {levelId} does not exist on the remote server");   
                }
                else
                {
                    Debug.LogError(error);
                }
                reject(error);
            });
        });
    }

    public async UniTask<bool> UnpackLevelPackage(string packagePath, string destFolder)
    {
        const int bufferSize = 256 * 1024;
        ZipStrings.CodePage = Encoding.UTF8.CodePage;
        try
        {
            Directory.CreateDirectory(destFolder);
        }
        catch (Exception error)
        {
            Debug.LogError("Failed to create level folder.");
            Debug.LogError(error);
            return false;
        }

        var fileName = Path.GetFileName(packagePath);
        var zipFileData = File.ReadAllBytes(packagePath);
        using (var fileStream = new MemoryStream())
        {
            ZipFile zipFile;
            try
            {
                fileStream.Write(zipFileData, 0, zipFileData.Length);
                fileStream.Flush();
                fileStream.Seek(0, SeekOrigin.Begin);

                zipFile = new ZipFile(fileStream);

                foreach (ZipEntry entry in zipFile)
                {
                    // Loop through all files to ensure the zip is valid
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Cannot read {fileName}. Is it a valid .zip archive file?");
                Debug.LogError(e.Message);
                return false;
            }

            foreach (ZipEntry entry in zipFile)
            {
                var targetFile = Path.Combine(destFolder, entry.Name);
                if (entry.Name.Contains("__MACOSX")) continue; // Fucking macOS...
                Debug.Log("Extracting " + entry.Name + "...");

                FileStream outputFile;
                try
                {
                    outputFile = File.Create(targetFile);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Cannot extract {entry.Name} from {fileName}. Is it a valid .zip archive file?");
                    Debug.LogError(e.Message);
                    return false;
                }

                using (outputFile)
                {
                    if (entry.Size <= 0) continue;
                    var zippedStream = zipFile.GetInputStream(entry);
                    var dataBuffer = new byte[bufferSize];

                    int readBytes;
                    while ((readBytes = zippedStream.Read(dataBuffer, 0, bufferSize)) > 0)
                    {
                        outputFile.Write(dataBuffer, 0, readBytes);
                        outputFile.Flush();
                        await UniTask.Yield(); // Prevent blocking main thread
                    }
                }
            }
        }

        return true;
    }

    public async UniTask LoadAllFromDataPath(bool clearExisting = true)
    {
        if (clearExisting)
        {
            LoadedLocalLevels.Clear();
            loadedPaths.Clear();
            Context.SpriteCache.DisposeTagged("LocalLevelCoverThumbnail");
        }

        var jsonPaths = Directory.GetFiles(Context.DataPath, "level.json", SearchOption.AllDirectories).ToList();
        Debug.Log($"Found {jsonPaths.Count} levels");

        await LoadFromMetadataFiles(jsonPaths);
    }

    public async UniTask<List<Level>> LoadFromMetadataFiles(List<string> jsonPaths)
    {
        var results = new List<Level>();
        int index;
        for (index = 0; index < jsonPaths.Count; index++)
        {
            var jsonPath = jsonPaths[index];
            if (loadedPaths.Contains(jsonPath))
            {
                Debug.Log($"Warning: {jsonPath} is already loaded!");
                continue;
            }
            
            var info = new FileInfo(jsonPath);
            if (info.Directory == null) continue;

            var path = info.Directory.FullName + Path.DirectorySeparatorChar;
            Debug.Log($"Loading {index + 1}/{jsonPaths.Count} from {path}");
            
            var meta = JsonConvert.DeserializeObject<LevelMeta>(File.ReadAllText(jsonPath));

            if (meta == null)
            {
                Debug.Log($"Invalid level.json file");
                Debug.Log($"Skipped {index + 1}/{jsonPaths.Count} from {path}");
                continue;
            }

            // Sort charts
            meta.SortCharts();

            // Reject invalid level meta
            if (!meta.Validate())
            {
                Debug.Log($"Invalid metadata");
                Debug.Log($"Skipped {index + 1}/{jsonPaths.Count} from {path}");
                continue;
            }

            var level = new Level(path, meta, info.LastWriteTimeUtc, Context.LocalPlayer.GetLastPlayedTime(meta.id));

            LoadedLocalLevels[meta.id] = level;
            loadedPaths.Add(jsonPath);
            results.Add(level);
            
            if (File.Exists(level.Path + ".thumbnail")) File.Delete(level.Path + ".thumbnail");
            if (!File.Exists(level.Path + CoverThumbnailFilename))
            {
                var thumbnailPath = "file://" + level.Path + level.Meta.background.path;

                using (var request = UnityWebRequestTexture.GetTexture(thumbnailPath))
                {
                    await request.SendWebRequest();
                    if (request.isNetworkError || request.isHttpError)
                    {
                        Debug.Log($"Cannot get background texture from {thumbnailPath}");
                        Debug.Log(request.error);
                        Debug.Log($"Skipped {index + 1}/{jsonPaths.Count}: {meta.id} ({path})");
                        continue;
                    }

                    var coverTexture = DownloadHandlerTexture.GetContent(request);

                    if (coverTexture == null)
                    {
                        Debug.Log($"Cannot get background texture from {thumbnailPath}");
                        Debug.Log(request.error);
                        Debug.Log($"Skipped {index + 1}/{jsonPaths.Count}: {meta.id} ({path})");
                        continue;
                    }
                    var croppedTexture = TextureScaler.FitCrop(coverTexture, Context.ThumbnailWidth, Context.ThumbnailHeight);
                    var bytes = croppedTexture.EncodeToJPG();
                    Object.Destroy(coverTexture);
                    Object.Destroy(croppedTexture);

                    await UniTask.DelayFrame(0); // Reduce load to prevent crash

                    File.WriteAllBytes(level.Path + CoverThumbnailFilename, bytes);
                }

                Debug.Log($"Thumbnail generated {index + 1}/{jsonPaths.Count}: {level.Id} ({thumbnailPath})");
            }
            
            Debug.Log($"Loaded {index + 1}/{jsonPaths.Count}: ({meta.id})");
            OnLevelLoadProgress.Invoke(level, index + 1, jsonPaths.Count);
        }

        return results;
    }

    private bool UpdateLevelMeta(Level level, LevelMeta meta)
    {
        var local = level.Meta;
        var remote = meta;
        
        var updated = false;
        if (local.version > remote.version)
        {
            Debug.Log($"Local version {local.version} > {remote.version}");
            return false;
        }

        if (local.schema_version != remote.schema_version)
        {
            local.schema_version = remote.schema_version;
            updated = true;
        }

        if (remote.title != null && local.title != remote.title)
        {
            local.title = remote.title;
            updated = true;
        }

        if (remote.title_localized != null && local.title_localized != remote.title_localized)
        {
            local.title_localized = remote.title_localized;
            updated = true;
        }

        if (remote.artist != null && local.artist != remote.artist)
        {
            local.artist = remote.artist;
            updated = true;
        }

        if (remote.artist_localized != null && local.artist_localized != remote.artist_localized)
        {
            local.artist_localized = remote.artist_localized;
            updated = true;
        }

        if (remote.artist_source != null && local.artist_source != remote.artist_source)
        {
            local.artist_source = remote.artist_source;
            updated = true;
        }

        if (remote.illustrator != null && local.illustrator != remote.illustrator)
        {
            local.illustrator = remote.illustrator;
            updated = true;
        }

        if (remote.illustrator_source != null && local.illustrator_source != remote.illustrator_source)
        {
            local.illustrator_source = remote.illustrator_source;
            updated = true;
        }

        if (remote.charter != null && local.charter != remote.charter)
        {
            local.charter = remote.charter;
            updated = true;
        }

        foreach (var type in new List<string> {LevelMeta.Easy, LevelMeta.Hard, LevelMeta.Extreme})
        {
            if (remote.GetChartSection(type) != null && local.GetChartSection(type) != null &&
                local.GetChartSection(type).difficulty != remote.GetChartSection(type).difficulty)
            {
                local.GetChartSection(type).difficulty = remote.GetChartSection(type).difficulty;
                updated = true;
            }
        }
        
        if (updated)
        {
            File.WriteAllText($"{level.Path}/level.json", JsonConvert.SerializeObject(local));
        }

        return updated;
    }
}

public class LevelEvent : UnityEvent<Level>
{
}

public class LevelLoadProgressEvent : UnityEvent<Level, int, int>
{
}