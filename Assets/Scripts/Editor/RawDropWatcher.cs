using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Ticket 153 — watch Drop/{Pickable,Nodes} (project root, outside Assets/)
/// for a raw Meshy zip or unzipped FBX, unzip if needed, run the ticket 152
/// bake as a background subprocess, then MOVE baked output into 148's
/// _Incoming folders. Ticket 163: Drop/Decimate/{Pickable,Nodes} is the same
/// zip flow with --decimate on the bake. Does not guess Pickable vs Node.
/// Does not duplicate 148 organize or 152 bake logic.
/// </summary>
public static class RawDropWatcher
{
    public const string DropPickable = "Drop/Pickable";
    public const string DropNodes = "Drop/Nodes";
    public const string DropDecimatePickable = "Drop/Decimate/Pickable";
    public const string DropDecimateNodes = "Drop/Decimate/Nodes";
    public const string BakeScript = "BlenderSource/Bake/_bake_fbx.py";
    public const string IncomingPickable = ObjectIncomingPostprocessor.IncomingPickable;
    public const string IncomingNodes = ObjectIncomingPostprocessor.IncomingNodes;

    public const string BlenderPrefsKey = "BeatTheMan.BlenderExecutable";
    public const string DefaultBlender = "/Applications/Blender.app/Contents/MacOS/Blender";

    const string LogPrefix = "[RawDrop]";
    const int StableMs = 500;
    const int BakePollMs = 250;

    static readonly string[] TextureExts =
    {
        ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".exr", ".psd", ".bmp"
    };

    static FileSystemWatcher watcherPickable;
    static FileSystemWatcher watcherNodes;
    static FileSystemWatcher watcherDecimatePickable;
    static FileSystemWatcher watcherDecimateNodes;
    static bool scanQueued;
    static bool scanning;
    static bool bakeRunning;
    static readonly Queue<DropJob> Queue = new Queue<DropJob>();
    static BakeState activeBake;
    static double nextBakePollAt;

    struct DropJob
    {
        public string Kind;
        public string Stem;
        public string SourcePath;
        public bool IsZip;
        public bool IsFolder;
        public bool Decimate;
    }

    class BakeState
    {
        public DropJob Job;
        public string InputFbx;
        public string OutputFbx;
        public string UnpackDir;
        public Process Process;
        public StringBuilder Log = new StringBuilder();
    }

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureFolders();
            HookWatchers();
            if (!Application.isBatchMode)
                QueueScan();
        };
        AssemblyReloadEvents.beforeAssemblyReload += TeardownWatchers;
        EditorApplication.update += OnEditorUpdate;
    }

    [MenuItem("Tools/Raw Drop/Scan Now")]
    public static void MenuScan()
    {
        EnsureFolders();
        QueueScan();
    }

    [MenuItem("Tools/Raw Drop/Choose Blender Executable…")]
    public static void MenuChooseBlender()
    {
        string current = GetBlenderPath();
        string dir = File.Exists(current) ? Path.GetDirectoryName(current) : "/Applications";
        string picked = EditorUtility.OpenFilePanel("Blender executable", dir, "");
        if (string.IsNullOrEmpty(picked))
            return;
        EditorPrefs.SetString(BlenderPrefsKey, picked);
        Debug.Log(LogPrefix + " Blender path set to " + Rel(picked));
    }

    /// <summary>
    /// Batch: Unity -batchmode -executeMethod RawDropWatcher.RunFromBatch
    /// Processes Drop/ synchronously, then hands off to 148.
    /// </summary>
    public static void RunFromBatch()
    {
        EnsureFolders();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            scanQueued = false;
            ScanIntoQueue();
            if (Queue.Count > 0)
                break;
            if (!scanQueued)
                break;
            System.Threading.Thread.Sleep(StableMs);
        }
        while (Queue.Count > 0)
            ProcessJobBlocking(Queue.Dequeue());
        AssetDatabase.Refresh();
        ObjectIncomingPostprocessor.OrganizeNow();
        Debug.Log(LogPrefix + " batch scan finished");
    }

    static void TeardownWatchers()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= TeardownWatchers;
        DisposeWatcher(ref watcherPickable);
        DisposeWatcher(ref watcherNodes);
        DisposeWatcher(ref watcherDecimatePickable);
        DisposeWatcher(ref watcherDecimateNodes);
    }

    static void DisposeWatcher(ref FileSystemWatcher w)
    {
        if (w == null)
            return;
        w.EnableRaisingEvents = false;
        w.Created -= OnFsEvent;
        w.Changed -= OnFsEvent;
        w.Renamed -= OnRenamed;
        w.Dispose();
        w = null;
    }

    static void HookWatchers()
    {
        DisposeWatcher(ref watcherPickable);
        DisposeWatcher(ref watcherNodes);
        DisposeWatcher(ref watcherDecimatePickable);
        DisposeWatcher(ref watcherDecimateNodes);
        watcherPickable = MakeWatcher(Abs(DropPickable));
        watcherNodes = MakeWatcher(Abs(DropNodes));
        watcherDecimatePickable = MakeWatcher(Abs(DropDecimatePickable));
        watcherDecimateNodes = MakeWatcher(Abs(DropDecimateNodes));
    }

    static FileSystemWatcher MakeWatcher(string absDir)
    {
        Directory.CreateDirectory(absDir);
        var w = new FileSystemWatcher(absDir);
        w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            | NotifyFilters.LastWrite | NotifyFilters.Size;
        w.IncludeSubdirectories = false;
        w.Created += OnFsEvent;
        w.Changed += OnFsEvent;
        w.Renamed += OnRenamed;
        w.EnableRaisingEvents = true;
        return w;
    }

    static void OnRenamed(object sender, RenamedEventArgs e)
    {
        RequestScanFromWatcher();
    }

    static void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        RequestScanFromWatcher();
    }

    static void RequestScanFromWatcher()
    {
        scanQueued = true;
    }

    static void QueueScan()
    {
        if (scanQueued)
            return;
        scanQueued = true;
    }

    static void OnEditorUpdate()
    {
        if (scanQueued && !scanning && !bakeRunning && !EditorApplication.isCompiling)
        {
            scanQueued = false;
            ScanIntoQueue();
        }

        if (!bakeRunning && Queue.Count > 0 && !EditorApplication.isCompiling
            && !Application.isBatchMode)
        {
            StartBakeAsync(Queue.Dequeue());
        }

        if (bakeRunning && activeBake != null && EditorApplication.timeSinceStartup >= nextBakePollAt)
        {
            nextBakePollAt = EditorApplication.timeSinceStartup + BakePollMs / 1000.0;
            PollBake();
        }
    }

    static void EnsureFolders()
    {
        Directory.CreateDirectory(Abs(DropPickable));
        Directory.CreateDirectory(Abs(DropNodes));
        Directory.CreateDirectory(Abs(DropDecimatePickable));
        Directory.CreateDirectory(Abs(DropDecimateNodes));
        Directory.CreateDirectory(Abs("Drop/_Unpacked/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_Unpacked/Nodes"));
        Directory.CreateDirectory(Abs("Drop/_Unpacked/Decimate/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_Unpacked/Decimate/Nodes"));
        Directory.CreateDirectory(Abs("Drop/_BakeOut"));
        Directory.CreateDirectory(Abs("Drop/_NeedsReview/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_NeedsReview/Nodes"));
        Directory.CreateDirectory(Abs("Drop/_NeedsReview/Decimate/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_NeedsReview/Decimate/Nodes"));
        Directory.CreateDirectory(Abs("Drop/_Processed/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_Processed/Nodes"));
        Directory.CreateDirectory(Abs("Drop/_Processed/Decimate/Pickable"));
        Directory.CreateDirectory(Abs("Drop/_Processed/Decimate/Nodes"));
        TouchGitkeep(Abs(DropPickable));
        TouchGitkeep(Abs(DropNodes));
        TouchGitkeep(Abs(DropDecimatePickable));
        TouchGitkeep(Abs(DropDecimateNodes));
    }

    static void TouchGitkeep(string dir)
    {
        string p = Path.Combine(dir, ".gitkeep");
        if (!File.Exists(p))
            File.WriteAllText(p, "");
    }

    static void ScanIntoQueue()
    {
        if (scanning)
            return;
        scanning = true;
        try
        {
            ScanKind("Pickable", Abs(DropPickable), false);
            ScanKind("Nodes", Abs(DropNodes), false);
            ScanKind("Pickable", Abs(DropDecimatePickable), true);
            ScanKind("Nodes", Abs(DropDecimateNodes), true);
        }
        finally
        {
            scanning = false;
        }
    }

    static void ScanKind(string kind, string absDir, bool decimate)
    {
        if (!Directory.Exists(absDir))
            return;

        foreach (string file in Directory.GetFiles(absDir))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith(".", StringComparison.Ordinal) || name == ".gitkeep")
                continue;
            if (name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsFileStable(file))
                {
                    QueueScan();
                    return;
                }
                EnqueueUnique(new DropJob
                {
                    Kind = kind,
                    Stem = SanitizeStem(Path.GetFileNameWithoutExtension(file)),
                    SourcePath = file,
                    IsZip = true,
                    Decimate = decimate
                });
                continue;
            }

            if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsFileStable(file))
                {
                    QueueScan();
                    return;
                }
                EnqueueUnique(new DropJob
                {
                    Kind = kind,
                    Stem = SanitizeStem(Path.GetFileNameWithoutExtension(file)),
                    SourcePath = file,
                    IsZip = false,
                    Decimate = decimate
                });
            }
        }

        foreach (string dir in Directory.GetDirectories(absDir))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith(".", StringComparison.Ordinal) || name.StartsWith("_", StringComparison.Ordinal))
                continue;
            var fbxs = FindFbxs(dir);
            if (fbxs.Count == 0)
                continue;
            bool stable = true;
            foreach (string fbx in fbxs)
            {
                if (!IsFileStable(fbx))
                {
                    stable = false;
                    break;
                }
            }
            if (!stable)
            {
                QueueScan();
                return;
            }
            EnqueueUnique(new DropJob
            {
                Kind = kind,
                Stem = SanitizeStem(name),
                SourcePath = dir,
                IsFolder = true,
                Decimate = decimate
            });
        }
    }

    static void EnqueueUnique(DropJob job)
    {
        if (string.IsNullOrEmpty(job.Stem) || string.IsNullOrEmpty(job.SourcePath))
            return;
        if (activeBake != null && PathsEqual(activeBake.Job.SourcePath, job.SourcePath))
            return;
        foreach (DropJob q in Queue)
        {
            if (PathsEqual(q.SourcePath, job.SourcePath))
                return;
        }
        Queue.Enqueue(job);
        Debug.Log(LogPrefix + " queued " + (job.Decimate ? "Decimate/" : "") + job.Kind + "/" + job.Stem);
    }

    static void ProcessJobBlocking(DropJob job)
    {
        if (!PrepareAndStart(job, out BakeState state))
            return;
        try
        {
            state.Process.WaitForExit();
            FinishBake(state, state.Process.ExitCode);
        }
        finally
        {
            try { state.Process.Dispose(); } catch { }
            bakeRunning = false;
            activeBake = null;
        }
    }

    static void StartBakeAsync(DropJob job)
    {
        if (!PrepareAndStart(job, out BakeState state))
            return;
        activeBake = state;
        bakeRunning = true;
        nextBakePollAt = EditorApplication.timeSinceStartup + BakePollMs / 1000.0;
    }

    static void PollBake()
    {
        BakeState state = activeBake;
        if (state == null || state.Process == null)
        {
            bakeRunning = false;
            activeBake = null;
            return;
        }
        if (!state.Process.HasExited)
            return;
        int code = state.Process.ExitCode;
        try { state.Process.Dispose(); } catch { }
        bakeRunning = false;
        activeBake = null;
        FinishBake(state, code);
    }

    static bool PrepareAndStart(DropJob job, out BakeState state)
    {
        state = null;
        string blender = GetBlenderPath();
        if (string.IsNullOrEmpty(blender) || !File.Exists(blender))
        {
            FailToNeedsReview(job, null, "blender_missing",
                "Blender executable not found. Set it via Tools/Raw Drop/Choose Blender Executable.");
            return false;
        }

        string script = Abs(BakeScript);
        if (!File.Exists(script))
        {
            FailToNeedsReview(job, null, "bake_script_missing", Rel(script) + " is missing");
            return false;
        }

        string unpackDir = Abs("Drop/_Unpacked/" + FlowPrefix(job) + job.Kind + "/" + job.Stem);
        string inputFbx;
        if (job.IsZip)
        {
            if (!TryUnzip(job, unpackDir, out string unzipErr, out inputFbx))
            {
                FailToNeedsReview(job, unpackDir, unzipErr.StartsWith("zip_slip") ? "zip_slip"
                    : unzipErr.StartsWith("multiple_fbx") ? "multiple_fbx"
                    : unzipErr.StartsWith("no_fbx") ? "no_fbx"
                    : "corrupt_zip", unzipErr);
                return false;
            }
        }
        else if (job.IsFolder)
        {
            var fbxs = FindFbxs(job.SourcePath);
            if (fbxs.Count != 1)
            {
                FailToNeedsReview(job, null, fbxs.Count == 0 ? "no_fbx" : "multiple_fbx",
                    "folder contains " + fbxs.Count + " FBX files (want exactly 1)");
                return false;
            }
            inputFbx = fbxs[0];
            unpackDir = job.SourcePath;
        }
        else
        {
            inputFbx = job.SourcePath;
            unpackDir = Path.GetDirectoryName(job.SourcePath);
        }

        string bakeDir = Abs("Drop/_BakeOut/" + job.Stem);
        Directory.CreateDirectory(bakeDir);
        string outFbx = Path.Combine(bakeDir, Path.GetFileName(inputFbx));

        string extra = "";
        if (job.Decimate)
            extra = " --decimate --kind " + job.Kind;
        var psi = new ProcessStartInfo
        {
            FileName = blender,
            Arguments = "--background --python \"" + script + "\" -- --input \""
                + inputFbx + "\" --output \"" + outFbx + "\"" + extra,
            WorkingDirectory = ProjectRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Process proc;
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = false };
            var bake = new BakeState
            {
                Job = job,
                InputFbx = inputFbx,
                OutputFbx = outFbx,
                UnpackDir = unpackDir,
                Process = proc
            };
            state = bake;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) bake.Log.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) bake.Log.AppendLine(e.Data); };
            if (!proc.Start())
            {
                FailToNeedsReview(job, unpackDir, "blender_start_failed", "Process.Start returned false");
                return false;
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            FailToNeedsReview(job, unpackDir, "blender_start_failed", ex.Message);
            return false;
        }

        Debug.Log(LogPrefix + " baking " + job.Kind + "/" + job.Stem + " → " + Rel(outFbx));
        return true;
    }

    static void FinishBake(BakeState state, int exitCode)
    {
        DropJob job = state.Job;
        string logPath = Path.Combine(Path.GetDirectoryName(state.OutputFbx) ?? Abs("Drop/_BakeOut"), "bake.log");
        try { File.WriteAllText(logPath, state.Log.ToString()); } catch { }

        if (exitCode == 0)
        {
            if (!File.Exists(state.OutputFbx))
            {
                FailToNeedsReview(job, state.UnpackDir, "bake_no_output",
                    "exit 0 but baked FBX missing: " + Rel(state.OutputFbx));
                return;
            }
            HandoffToIncoming(job, state);
            ArchiveOriginals(job, state, "Drop/_Processed/" + FlowPrefix(job) + job.Kind + "/" + job.Stem);
            Debug.Log(LogPrefix + " baked " + (job.Decimate ? "Decimate/" : "") + job.Kind + "/" + job.Stem
                + " → " + IncomingFolder(job.Kind) + "/" + job.Stem);
            return;
        }

        string reason = exitCode == 2 ? "bake_exit_2" : "bake_exit_1";
        string detail = exitCode == 2
            ? "152 safety net (keyword, mesh_count>1, or 163 decimate stall). In-Unity copy is Assets/Objects/_Review. Raw drop moved to Drop/_NeedsReview."
            : "152 bake failed (exit " + exitCode + ")";
        FailToNeedsReview(job, state.UnpackDir, reason, detail);
    }

    static void HandoffToIncoming(DropJob job, BakeState state)
    {
        string incomingKind = IncomingFolder(job.Kind);
        string destDir = Abs(incomingKind + "/" + job.Stem);
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, true);
        Directory.CreateDirectory(destDir);

        string bakeDir = Path.GetDirectoryName(state.OutputFbx);
        foreach (string file in Directory.GetFiles(bakeDir))
        {
            string name = Path.GetFileName(file);
            if (name.Equals("bake.log", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(".bake.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || IsTextureFile(file))
                File.Copy(file, Path.Combine(destDir, name), true);
        }

        AssetDatabase.Refresh();
        if (!Application.isBatchMode)
            ObjectIncomingPostprocessor.OrganizeNow();
    }

    static void FailToNeedsReview(DropJob job, string unpackDir, string reason, string detail)
    {
        string dest = Abs("Drop/_NeedsReview/" + FlowPrefix(job) + job.Kind + "/" + job.Stem);
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);

        try
        {
            if (job.IsZip && File.Exists(job.SourcePath))
                File.Copy(job.SourcePath, Path.Combine(dest, Path.GetFileName(job.SourcePath)), true);
            else if (job.IsFolder && Directory.Exists(job.SourcePath))
                CopyDirectory(job.SourcePath, Path.Combine(dest, Path.GetFileName(job.SourcePath)));
            else if (File.Exists(job.SourcePath))
            {
                CopyLooseSiblings(job.SourcePath, dest);
                File.Copy(job.SourcePath, Path.Combine(dest, Path.GetFileName(job.SourcePath)), true);
            }

            if (!string.IsNullOrEmpty(unpackDir) && Directory.Exists(unpackDir)
                && !PathsEqual(unpackDir, job.SourcePath)
                && !PathsEqual(unpackDir, Path.GetDirectoryName(job.SourcePath)))
            {
                string unpackDest = Path.Combine(dest, "_unpacked");
                CopyDirectory(unpackDir, unpackDest);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogPrefix + " copy to _NeedsReview failed: " + ex.Message);
        }

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"ticket\": 153,");
        json.AppendLine("  \"status\": \"needs_review\",");
        json.AppendLine("  \"reason\": " + JsonString(reason) + ",");
        json.AppendLine("  \"detail\": " + JsonString(detail) + ",");
        json.AppendLine("  \"kind\": " + JsonString(job.Kind) + ",");
        json.AppendLine("  \"decimate\": " + (job.Decimate ? "true" : "false") + ",");
        json.AppendLine("  \"stem\": " + JsonString(job.Stem) + ",");
        json.AppendLine("  \"source\": " + JsonString(Rel(job.SourcePath)));
        json.AppendLine("}");
        File.WriteAllText(Path.Combine(dest, "FAILED.json"), json.ToString());

        TryDeleteSource(job, unpackDir);
        Debug.LogWarning(LogPrefix + " " + job.Kind + "/" + job.Stem + " → Drop/_NeedsReview (" + reason + ")");
    }

    static void ArchiveOriginals(DropJob job, BakeState state, string destRel)
    {
        string dest = Abs(destRel);
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);
        try
        {
            if (job.IsZip && File.Exists(job.SourcePath))
                File.Move(job.SourcePath, Path.Combine(dest, Path.GetFileName(job.SourcePath)));
            else if (job.IsFolder && Directory.Exists(job.SourcePath)
                && !PathsEqual(job.SourcePath, dest))
            {
                // Source folder lives under Drop/Pickable — move aside.
                string moved = Path.Combine(dest, Path.GetFileName(job.SourcePath));
                Directory.Move(job.SourcePath, moved);
            }
            else if (File.Exists(job.SourcePath))
            {
                MoveLooseSiblings(job.SourcePath, dest);
                File.Move(job.SourcePath, Path.Combine(dest, Path.GetFileName(job.SourcePath)));
            }

            if (!string.IsNullOrEmpty(state.UnpackDir) && Directory.Exists(state.UnpackDir)
                && !PathsEqual(state.UnpackDir, job.SourcePath)
                && !PathsEqual(state.UnpackDir, Path.GetDirectoryName(job.SourcePath)))
            {
                TryDeleteDir(state.UnpackDir);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogPrefix + " archive originals: " + ex.Message);
            TryDeleteSource(job, state.UnpackDir);
        }
    }

    static void TryDeleteSource(DropJob job, string unpackDir)
    {
        try
        {
            if (job.IsZip && File.Exists(job.SourcePath))
                File.Delete(job.SourcePath);
            else if (job.IsFolder && Directory.Exists(job.SourcePath))
                Directory.Delete(job.SourcePath, true);
            else if (File.Exists(job.SourcePath))
            {
                DeleteLooseSiblings(job.SourcePath);
                File.Delete(job.SourcePath);
            }
            if (!string.IsNullOrEmpty(unpackDir) && Directory.Exists(unpackDir)
                && !PathsEqual(unpackDir, Path.GetDirectoryName(job.SourcePath)))
                TryDeleteDir(unpackDir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(LogPrefix + " could not clear drop source: " + ex.Message);
        }
    }

    static bool TryUnzip(DropJob job, string unpackDir, out string error, out string inputFbx)
    {
        error = null;
        inputFbx = null;
        TryDeleteDir(unpackDir);
        Directory.CreateDirectory(unpackDir);

        try
        {
            using (var zip = ZipFile.OpenRead(job.SourcePath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = (entry.FullName ?? "").Replace('\\', '/');
                    if (string.IsNullOrEmpty(name) || name.EndsWith("/"))
                        continue;
                    if (name.Contains("..") || Path.IsPathRooted(name) || name.StartsWith("/", StringComparison.Ordinal))
                    {
                        error = "zip_slip: refused entry " + name;
                        return false;
                    }
                }
            }
            ZipFile.ExtractToDirectory(job.SourcePath, unpackDir);
        }
        catch (InvalidDataException ex)
        {
            error = "corrupt_zip: " + ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            error = "corrupt_zip: " + ex.Message;
            return false;
        }

        var fbxs = FindFbxs(unpackDir);
        if (fbxs.Count == 0)
        {
            error = "no_fbx: zip extracted with 0 FBX files";
            return false;
        }
        if (fbxs.Count > 1)
        {
            error = "multiple_fbx: zip contains " + fbxs.Count + " FBX files (want exactly 1)";
            return false;
        }
        inputFbx = fbxs[0];
        return true;
    }

    static List<string> FindFbxs(string dir)
    {
        var list = new List<string>();
        if (!Directory.Exists(dir))
            return list;
        foreach (string file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                list.Add(file);
        }
        return list;
    }

    static void CopyLooseSiblings(string fbx, string destDir)
    {
        string dir = Path.GetDirectoryName(fbx);
        string stem = Path.GetFileNameWithoutExtension(fbx);
        if (dir == null)
            return;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.Equals(fbx, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsTextureFile(file))
                continue;
            string fileStem = Path.GetFileNameWithoutExtension(file);
            if (fileStem.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || fileStem.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
    }

    static void MoveLooseSiblings(string fbx, string destDir)
    {
        string dir = Path.GetDirectoryName(fbx);
        string stem = Path.GetFileNameWithoutExtension(fbx);
        if (dir == null)
            return;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.Equals(fbx, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsTextureFile(file))
                continue;
            string fileStem = Path.GetFileNameWithoutExtension(file);
            if (fileStem.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || fileStem.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
                File.Move(file, Path.Combine(destDir, Path.GetFileName(file)));
        }
    }

    static void DeleteLooseSiblings(string fbx)
    {
        string dir = Path.GetDirectoryName(fbx);
        string stem = Path.GetFileNameWithoutExtension(fbx);
        if (dir == null)
            return;
        foreach (string file in Directory.GetFiles(dir))
        {
            if (file.Equals(fbx, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsTextureFile(file))
                continue;
            string fileStem = Path.GetFileNameWithoutExtension(file);
            if (fileStem.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || fileStem.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
        }
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch { }
    }

    static bool IsFileStable(string absPath)
    {
        if (!File.Exists(absPath))
            return false;
        var info = new FileInfo(absPath);
        if (info.Length <= 0)
            return false;
        return (DateTime.UtcNow - info.LastWriteTimeUtc).TotalMilliseconds >= StableMs;
    }

    static bool IsTextureFile(string path)
    {
        string ext = Path.GetExtension(path);
        for (int i = 0; i < TextureExts.Length; i++)
        {
            if (ext.Equals(TextureExts[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string SanitizeStem(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        string name = raw.Trim();
        if (name == "." || name == ".." || name == ".gitkeep")
            return null;
        char[] bad = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        foreach (char c in bad)
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    static string GetBlenderPath()
    {
        string prefs = EditorPrefs.GetString(BlenderPrefsKey, "");
        if (!string.IsNullOrEmpty(prefs))
            return prefs;
        return DefaultBlender;
    }

    static string FlowPrefix(DropJob job)
    {
        return job.Decimate ? "Decimate/" : "";
    }

    static string IncomingFolder(string kind)
    {
        return string.Equals(kind, "Nodes", StringComparison.OrdinalIgnoreCase)
            ? IncomingNodes
            : IncomingPickable;
    }

    static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    static string Abs(string projectRelative)
    {
        return Path.GetFullPath(Path.Combine(ProjectRoot(), projectRelative.Replace('/', Path.DirectorySeparatorChar)));
    }

    static string Rel(string absPath)
    {
        if (string.IsNullOrEmpty(absPath))
            return absPath;
        string root = ProjectRoot();
        string full = Path.GetFullPath(absPath);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            string rel = full.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            return rel.Replace('\\', '/');
        }
        return absPath.Replace('\\', '/');
    }

    static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    static string JsonString(string s)
    {
        if (s == null)
            return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
