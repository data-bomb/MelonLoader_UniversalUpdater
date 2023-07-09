﻿/*
Universal Mod Updater Plugin
Copyright (C) 2023 by databomb

* Description *
Checks each DLL file in the Mods\ directory for the assemblyInfo 
optional downloadLink URL. If a downloadLink URL is found then it 
will try and check for an updater.json file and download the latest
version, if needed.

* License *
This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using MelonLoader;
using Newtonsoft.Json;
using MelonLoader.Utils;
using UniversalUpdater;
using System.Reflection;
using Mono.Cecil;
using System.Net.Http.Headers;
using System.Net.Http;

[assembly: MelonInfo(typeof(Updater), "Universal Mod Updater", "1.1.7", "databomb")]
[assembly: MelonGame(null, null)]

namespace UniversalUpdater
{
    public class Updater : MelonPlugin
    {

        public class UpdaterEntry
        {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            public String Version
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            {
                get;
                set;
            }
            public String? RemotePath
            {
                get;
                set;
            }
            public String? UpdateNotes
            {
                get;
                set;
            }
            public bool StoreBackup
            {
                get;
                set;
            }
            public DependencyEntry[]? DependencyEntries
            {
                get;
                set;
            }
        }

        public class DependencyEntry
        {
            public String? RemoteURL
            {
                get;
                set;
            }
            public String? LocalPath
            {
                get;
                set;
            }
            public bool ForceUpdate
            {
                get;
                set;
            }
        }

        public class MelonInfoAttributeExtended
        {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            public MelonInfoAttribute Attr
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            {
                get;
                set;
            }
            public String? Namespace
            {
                get;
                set;
            }
            public String? Class
            {
                get;
                set;
            }
        }

        // there are 4 required parameters and 1 optional parameter
        // https://melonwiki.xyz/#/modders/attributes
        // type isn't available yet so extend to add 2 optional parameters for namespace and class
        static MelonInfoAttributeExtended? GetMelonModAttributes(String fullModFilePath)
        {
            AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(fullModFilePath);
            CustomAttribute? customAttribute = assemblyDefinition.CustomAttributes.First(k => k.AttributeType.FullName == "MelonLoader.MelonInfoAttribute");
            assemblyDefinition.Dispose();

            if (customAttribute != null)
            {
                MelonInfoAttribute attributesOriginal = new(customAttribute.ConstructorArguments[0].Value.GetType(),
                                                            (String)customAttribute.ConstructorArguments[1].Value,
                                                            (String)customAttribute.ConstructorArguments[2].Value,
                                                            (String)customAttribute.ConstructorArguments[3].Value,
                                                            (String)customAttribute.ConstructorArguments[4].Value);

                MelonInfoAttributeExtended attributesExtended = new()
                {
                    Attr = attributesOriginal,
                    Namespace = customAttribute.ConstructorArguments[0].Value.ToString().Split('.')[0],
                    Class = customAttribute.ConstructorArguments[0].Value.ToString().Split('.')[1]
                };

                return attributesExtended;
            }

            return null;
        }

        static bool IsNewerVersion(Version existingVersion, Version checkVersion)
        {
            if (existingVersion.CompareTo(checkVersion) < 0)
            {
                return true;
            }

            return false;
        }

        static bool ShouldOverwriteBackup(String backupFile, Version currentVersion)
        {
            MelonInfoAttributeExtended? modAttributes = GetMelonModAttributes(backupFile);
            if (modAttributes == null)
            {
                return true;
            }

            Version backupVersion = new(modAttributes.Attr.Version);
            if (IsNewerVersion(backupVersion, currentVersion))
            {
                return true;
            }

            return false;
        }

        static String FormatURLString(String downloadLink, String modNamespace, String subPath)
        {
            // check for GitHub and translate to raw URL
            if (downloadLink.StartsWith("https://github.com/"))
            {
                String githubAccount = downloadLink.Split('/')[3];
                String githubRepo = downloadLink.Split('/')[4];
                return $"https://raw.githubusercontent.com/{githubAccount}/{githubRepo}/main/{modNamespace}/{subPath}";
            }
            else
            {
                return $"{downloadLink}/{modNamespace}/{subPath}";
            }
        }

        static UpdaterEntry? GetUpdaterEntry(HttpClient updaterClient, String downloadLink, String modNamespace)
        {
            String updaterText = "";

            // build URL
            String updateURL = FormatURLString(downloadLink, modNamespace, "updater.json");
            MelonLogger.Msg(updateURL);

            try
            {
                updaterText = updaterClient.GetStringAsync(updateURL).Result;
            }
            catch
            {
                MelonLogger.Msg("Updater not found for " + modNamespace);
                return null;
            }

            MelonLogger.Msg(updaterText);
            UpdaterEntry? thisUpdater = JsonConvert.DeserializeObject<UpdaterEntry>(updaterText);

            return thisUpdater;
        }

        static void DownloadFile(HttpClient updaterClient, String fileURL, FileInfo theFile)
        {
            MelonLogger.Msg(fileURL);

            Stream downloadStream = updaterClient.GetStreamAsync(fileURL).Result;
            FileStream fileStream = new(theFile.FullName, FileMode.Create);
            downloadStream.CopyTo(fileStream);

            MelonLogger.Msg("Update for " + theFile.Name + " complete.");
        }

        static void MakeModBackup(FileInfo theMod, Version theVersion)
        {
            String backupDirectory = System.IO.Path.Combine(MelonEnvironment.ModsDirectory, @"backup\");
            if (!System.IO.Directory.Exists(backupDirectory))
            {
                MelonLogger.Msg("Creating backup directory at: " + backupDirectory);
                System.IO.Directory.CreateDirectory(backupDirectory);
            }

            MelonLogger.Msg("Moving " + theMod.Name + " to backup directory");
            String backupFilePath = Path.Combine(backupDirectory, theMod.Name);
            if (System.IO.File.Exists(backupFilePath))
            {
                if (ShouldOverwriteBackup(backupFilePath, theVersion))
                {
                    System.IO.File.Move(theMod.FullName, backupFilePath, true);
                }
            }
            else
            {
                System.IO.File.Move(theMod.FullName, backupFilePath);
            }
        }

        // iterate through mod files
        public override void OnPreInitialization() 
        {
            try
            {
                string modsPath = MelonEnvironment.ModsDirectory;
                DirectoryInfo modsDirectory = new(modsPath);
                FileInfo[] modFiles = modsDirectory.GetFiles("*.dll");

                HttpClient updaterClient = new();
                updaterClient.DefaultRequestHeaders.Add("User-Agent", "MelonUpdater");
                updaterClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    Private = true,
                    NoStore = true
                };

                for (int i = 0; i < modFiles.Length; i++)
                {
                    FileInfo thisMod = modFiles[i];

                    // read assembly info without loading the mod
                    MelonInfoAttributeExtended? modAttributes = GetMelonModAttributes(thisMod.FullName);
                    if (modAttributes == null)
                    {
                        MelonLogger.Warning("Could not find MelonMod attributes for " + thisMod.Name);
                        continue;
                    }

                    if (modAttributes.Attr.DownloadLink == null)
                    {
                        MelonLogger.Msg("No download URL found for " + thisMod.Name);
                        continue;
                    }

                    if (!modAttributes.Attr.DownloadLink.StartsWith("http"))
                    {
                        MelonLogger.Warning("Invalid URL found for " + thisMod.Name);
                        continue;
                    }

                    MelonLogger.Msg(modAttributes.Namespace + "." + modAttributes.Class + " " + modAttributes.Attr.Name + " " + modAttributes.Attr.Version + " " + modAttributes.Attr.Author + " " + modAttributes.Attr.DownloadLink);

                    // attempt to grab the deserialized json
                    UpdaterEntry? thisUpdater = GetUpdaterEntry(updaterClient, modAttributes.Attr.DownloadLink, modAttributes.Namespace);
                    if (thisUpdater == null)
                    {
                        MelonLogger.Msg("Skipping " + thisMod.Name + " due to json object corruption");
                        continue;
                    }

                    Version updaterVersion = new(thisUpdater.Version);
                    Version currentVersion = new(modAttributes.Attr.Version);
                    // do we already have the latest version?
                    if (!IsNewerVersion(currentVersion, updaterVersion))
                    {
                        MelonLogger.Msg("Skipping " + thisMod.Name + " due to already having latest version");
                        continue;
                    }

                    MelonLogger.Msg("Updating " + thisMod.Name + "...");

                    if (thisUpdater.UpdateNotes != null && thisUpdater.UpdateNotes.Length > 0)
                    {
                        MelonLogger.Msg(thisMod.Name + " Patch Notes- " + thisUpdater.UpdateNotes);
                    }

                    if (thisUpdater.StoreBackup)
                    {
                        MakeModBackup(thisMod, currentVersion);
                    }

                    // download new and replace
                    String binaryPath = $"{thisUpdater.RemotePath}/{thisMod.Name}";
                    String fileURL = FormatURLString(modAttributes.Attr.DownloadLink, modAttributes.Namespace, binaryPath);
                    DownloadFile(updaterClient, fileURL, thisMod);

                    // TODO: deal with dependencies

                    }

                updaterClient.Dispose();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(ex.ToString());
            }
        }
    }
}