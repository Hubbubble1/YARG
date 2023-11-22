﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using YARG.Core.Game;
using YARG.Core.Utility;
using YARG.Helpers;
using YARG.Menu.Persistent;

namespace YARG.Settings.Customization
{
    public abstract class CustomContent
    {
        private static readonly Regex _fileNameSanitize = new("([^a-zA-Z0-9])", RegexOptions.Compiled);

        protected static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
            Converters = new List<JsonConverter>
            {
                new JsonColorConverter()
            }
        };

        public readonly string ContentDirectory;

        public abstract IReadOnlyList<BasePreset> DefaultBasePresets { get; }
        public abstract IReadOnlyList<BasePreset> CustomBasePresets { get; }

        protected CustomContent(string contentDirectory)
        {
            Directory.CreateDirectory(contentDirectory);
            ContentDirectory = contentDirectory;
        }

        public abstract void AddPreset(BasePreset preset);
        public abstract void DeletePreset(BasePreset preset);
        public abstract void RenamePreset(BasePreset preset, string name);

        public abstract void ReloadPresetAtPath(string path);

        public abstract void SetSettingsFromPreset(BasePreset preset);
        public abstract void SetPresetFromSettings(BasePreset preset);

        public abstract void SaveAll();

        public abstract BasePreset GetBasePresetById(Guid guid);
        public abstract bool HasPresetId(Guid guid);

        public abstract void ExportPreset(BasePreset preset, string path);
        public abstract BasePreset ImportPreset(string path);

        /// <summary>
        /// Adds all of the presets to the specified dropdown.
        /// </summary>
        /// <returns>
        /// A list containing all of the base presets in order as shown in the dropdown.
        /// </returns>
        public List<BasePreset> AddOptionsToDropdown(TMP_Dropdown dropdown)
        {
            var list = new List<BasePreset>();

            dropdown.options.Clear();

            // Add defaults
            foreach (var preset in DefaultBasePresets)
            {
                dropdown.options.Add(new($"<color=#1CCFFF>{preset.Name}</color>"));
                list.Add(preset);
            }

            // Add customs
            foreach (var preset in CustomBasePresets)
            {
                dropdown.options.Add(new(preset.Name));
                list.Add(preset);
            }

            return list;
        }

        protected static string GetFileNameForPreset(BasePreset preset)
        {
            // Limit the file name to 20 characters
            string fileName = preset.Name;
            if (fileName.Length > 20)
            {
                fileName = fileName[..20];
            }

            // Remove symbols
            fileName = _fileNameSanitize.Replace(fileName, "_");

            // Add the end
            fileName += $".{preset.Id.ToString()[..8]}.json";

            return fileName;
        }
    }

    public abstract class CustomContent<T> : CustomContent where T : BasePreset
    {
        protected readonly List<T> Content = new();

        public abstract IReadOnlyList<T> DefaultPresets { get; }
        public override IReadOnlyList<BasePreset> DefaultBasePresets => DefaultPresets;

        public IReadOnlyList<T> CustomPresets => Content;
        public override IReadOnlyList<BasePreset> CustomBasePresets => CustomPresets;

        public abstract string PresetTypeStringName { get; }

        protected CustomContent(string contentDirectory) : base(contentDirectory)
        {
        }

        public override void AddPreset(BasePreset preset)
        {
            if (preset is T t)
            {
                // Skip if the user already has the preset
                if (HasPresetId(preset.Id))
                {
                    return;
                }

                Content.Add(t);
            }
            else
            {
                throw new InvalidOperationException("Attempted to add invalid preset type.");
            }
        }

        public override void DeletePreset(BasePreset preset)
        {
            if (preset is T t)
            {
                Content.Remove(t);
                DeletePresetFile(t);
            }
            else
            {
                throw new InvalidOperationException("Attempted to add invalid preset type.");
            }
        }

        public override void RenamePreset(BasePreset preset, string name)
        {
            if (preset is T t)
            {
                DeletePresetFile(t);
                t.Name = name;
            }
            else
            {
                throw new InvalidOperationException("Attempted to add invalid preset type.");
            }
        }

        public override void ReloadPresetAtPath(string path)
        {
            var preset = LoadFile(path);

            var loadedPreset = GetPresetById(preset.Id);

            if (loadedPreset is null)
            {
                // Just add the preset if it doesn't exist
                Content.Add(preset);
            }
            else
            {
                // Otherwise, reload it by removing it and re-adding it
                int index = Content.IndexOf(loadedPreset);
                Content.RemoveAt(index);
                Content.Insert(index, preset);
            }
        }

        public void LoadFiles()
        {
            Content.Clear();

            var renameList = new List<(string From, string To)>();

            PathHelper.SafeEnumerateFiles(ContentDirectory, "*.json", true, (path) =>
            {
                var preset = LoadFile(path);

                // If the path is incorrect, rename it
                var correctPath = GetFileNameForPreset(preset);
                if (Path.GetFileName(path) != correctPath)
                {
                    // We must do this after since we are in the middle of enumerating it
                    renameList.Add((path, Path.Join(ContentDirectory, correctPath)));
                }

                // See if preset already exists
                if (HasPresetId(preset.Id))
                {
                    Debug.LogWarning($"Duplicate preset `{path}` found!");
                    return true;
                }

                // Otherwise, add the preset
                Content.Add(preset);

                return true;
            });

            // Rename all files
            foreach (var (from, to) in renameList)
            {
                try
                {
                    if (!File.Exists(to))
                    {
                        // If the file doesn't exist, just rename it
                        File.Move(from, to);
                        Debug.Log($"Renamed preset file from `{from}` to its correct form.");
                    }
                    else
                    {
                        // If it does, delete the original file (since it's probably a duplicate)
                        File.Delete(from);
                        Debug.Log($"Deleted duplicate preset file `{from}`.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to move file `{from}`.");
                    Debug.LogException(e);
                }
            }
        }

        private string SavePresetFile(T preset)
        {
            preset.Type = PresetTypeStringName;
            var text = JsonConvert.SerializeObject(preset, JsonSettings);

            var path = Path.Join(ContentDirectory, GetFileNameForPreset(preset));

            File.WriteAllText(path, text);
            return path;
        }

        private void DeletePresetFile(T preset)
        {
            PathHelper.SafeEnumerateFiles(ContentDirectory, "*.json", true, (path) =>
            {
                var file = JsonConvert.DeserializeObject<T>(File.ReadAllText(path), JsonSettings);

                if (file.Id == preset.Id)
                {
                    File.Delete(path);
                    return false;
                }

                return true;
            });
        }

        public override void SaveAll()
        {
            foreach (var preset in CustomPresets)
            {
                SavePresetFile(preset);
            }
        }

        public T GetPresetById(Guid guid)
        {
            foreach (var preset in DefaultPresets)
            {
                if (preset.Id == guid) return preset;
            }

            foreach (var preset in CustomPresets)
            {
                if (preset.Id == guid) return preset;
            }

            return null;
        }

        public override BasePreset GetBasePresetById(Guid guid)
        {
            return GetPresetById(guid);
        }

        public override bool HasPresetId(Guid guid)
        {
            return GetPresetById(guid) is not null;
        }

        public override void ExportPreset(BasePreset preset, string path)
        {
            // Skip default presets, and presets that haven't been added
            if (!HasPresetId(preset.Id)) return;

            try
            {
                // Make sure the preset is saved, and get it's path
                string presetPath = SavePresetFile((T) preset);

                // Create a zip file
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

                // Add files to zip
                zip.CreateEntryFromFile(presetPath, "preset.json");
                AddAdditionalFilesToExport(zip);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Failed to export preset. See error below for more details.");
                Debug.LogException(e);
            }
        }

        protected virtual void AddAdditionalFilesToExport(ZipArchive archive)
        {
        }

        public override BasePreset ImportPreset(string path)
        {
            try
            {
                // Open zip file
                using var zip = ZipFile.Open(path, ZipArchiveMode.Read);

                // Get the preset entry, and read it
                var presetEntry = zip.GetEntry("preset.json");
                using var reader = new StreamReader(presetEntry!.Open());
                var preset = JsonConvert.DeserializeObject<T>(reader.ReadToEnd(), JsonSettings);

                if (preset.Type != PresetTypeStringName)
                {
                    DialogManager.Instance.ShowMessage("Cannot Import Preset",
                        "Wrong preset type! Are you selecting the right preset type?");
                    return null;
                }

                if (HasPresetId(preset.Id))
                {
                    DialogManager.Instance.ShowMessage("Cannot Import Preset",
                        "A preset with the same ID has already been imported!");
                    return null;
                }

                // Save additional files and modify preset
                SaveAdditionalFilesFromExport(zip, preset);

                // Save the preset
                AddPreset(preset);
                SavePresetFile(preset);

                return preset;
            }
            catch (Exception e)
            {
                DialogManager.Instance.ShowMessage("Cannot Import Preset",
                    "The selected preset is most likely corrupted, or is not a valid preset file.");

                Debug.LogWarning("Failed to import preset. See error below for more details.");
                Debug.LogException(e);

                return null;
            }
        }

        protected virtual void SaveAdditionalFilesFromExport(ZipArchive archive, T preset)
        {
        }

        private static T LoadFile(string path)
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), JsonSettings);
        }
    }
}