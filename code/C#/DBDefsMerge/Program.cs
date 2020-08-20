using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using DBDefsLib;
using static DBDefsLib.Structs;

namespace DBDefsMerge
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: <firstdir> <seconddir> <outdir>");
                Environment.Exit(1);
            }

            var numLayoutsAdded = 0;

            var firstDir = args[0];
            var secondDir = args[1];
            var targetDir = args[2];

            var firstDirFiles = new DirectoryInfo(firstDir).GetFiles().Select(o => o.Name).ToList();
            var secondDirFiles = new DirectoryInfo(secondDir).GetFiles().Select(o => o.Name).ToList();

            var firstDirFilesLC = new DirectoryInfo(firstDir).GetFiles().Select(o => o.Name.ToLower()).ToList();
            var secondDirFilesLC = new DirectoryInfo(secondDir).GetFiles().Select(o => o.Name.ToLower()).ToList();

            var newDefinitions = new Dictionary<string, DBDefinition>();

            var reader = new DBDReader();

            foreach (var file in secondDirFiles)
            {
                var dbName = Path.GetFileNameWithoutExtension(file);
                if (firstDirFilesLC.Contains(file.ToLower()))
                {
                    // Both directories have this file. Merge!
                    var firstFileName = Path.Combine(firstDir, firstDirFiles.ElementAt(firstDirFilesLC.IndexOf(file.ToLower())));
                    var firstFile = reader.Read(firstFileName);
                    var secondFile = reader.Read(Path.Combine(secondDir, file));

                    var newDefinition = firstFile;

                    // Merge column definitions
                    foreach (var columnDefinition2 in secondFile.columnDefinitions)
                    {
                        var foundCol = false;
                        foreach (var columnDefinition1 in firstFile.columnDefinitions)
                        {
                            if (Utils.NormalizeColumn(columnDefinition2.Key).ToLower() == Utils.NormalizeColumn(columnDefinition1.Key).ToLower())
                            {
                                foundCol = true;

                                if (columnDefinition2.Value.type != columnDefinition1.Value.type)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine("Types are different for (1)" + dbName + "::" + columnDefinition1.Key + " = " + columnDefinition1.Value.type + " and (2)" + dbName + "::" + columnDefinition2.Key + " = " + columnDefinition2.Value.type + ", using type " + columnDefinition2.Value.type + " from 2");

                                    // If this is an uncommon conversion (not uint -> int or vice versa) throw an error
                                    if ((columnDefinition1.Value.type == "uint" && columnDefinition2.Value.type == "int") || (columnDefinition1.Value.type == "int" && columnDefinition2.Value.type == "uint"))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine("Type difference for column (1)" + dbName + "::" + columnDefinition1.Key + " = " + columnDefinition1.Value.type + " and(2)" + dbName + "::" + columnDefinition2.Key + " = " + columnDefinition2.Value.type + ", ignoring..");
                                    }
                                    else
                                    {
                                        throw new Exception("bad type difference, refusing to handle");
                                    }

                                    Console.ResetColor();
                                }

                                if (columnDefinition2.Key != columnDefinition1.Key)
                                {
                                    if (Utils.NormalizeColumn(columnDefinition2.Key, true) == columnDefinition1.Key)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("Automagically fixed casing issue between (1)" + dbName + "::" + columnDefinition1.Key + " and (2)" + dbName + "::" + columnDefinition2.Key);
                                        for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                        {
                                            for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                            {
                                                if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                                {
                                                    secondFile.versionDefinitions[i].definitions[j].name = Utils.NormalizeColumn(columnDefinition2.Key, true);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine("Unable to automagically fix casing issue between (1)" + dbName + "::" + columnDefinition1.Key + " and (2)" + dbName + "::" + columnDefinition2.Key + ", falling back to (1) naming");
                                        for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                        {
                                            for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                            {
                                                if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                                {
                                                    secondFile.versionDefinitions[i].definitions[j].name = columnDefinition1.Key;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    Console.ResetColor();
                                }

                                // Merge comments
                                if (columnDefinition2.Value.comment != columnDefinition1.Value.comment)
                                {
                                    for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                    {
                                        for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                        {
                                            if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                            {
                                                var colDef = newDefinition.columnDefinitions[columnDefinition1.Key];

                                                if (columnDefinition2.Value.comment == null)
                                                {
                                                    colDef.comment = columnDefinition1.Value.comment;
                                                }
                                                else if (columnDefinition1.Value.comment == null)
                                                {
                                                    colDef.comment = columnDefinition2.Value.comment;
                                                }
                                                else
                                                {
                                                    throw new Exception("Do not support merging 2 comments yet!");
                                                }

                                                newDefinition.columnDefinitions[columnDefinition1.Key] = colDef;
                                            }
                                        }
                                    }
                                }

                                // Merge foreignTable/foreignKey
                                if (columnDefinition2.Value.foreignTable != columnDefinition1.Value.foreignTable || columnDefinition2.Value.foreignColumn != columnDefinition1.Value.foreignColumn)
                                {
                                    for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                    {
                                        for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                        {
                                            if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                            {
                                                var colDef = newDefinition.columnDefinitions[columnDefinition1.Key];

                                                if (columnDefinition2.Value.foreignTable == null && columnDefinition2.Value.foreignColumn == null)
                                                {
                                                    colDef.foreignTable = columnDefinition1.Value.foreignTable;
                                                    colDef.foreignColumn = columnDefinition1.Value.foreignColumn;
                                                }
                                                else if (columnDefinition1.Value.foreignTable == null && columnDefinition1.Value.foreignColumn == null)
                                                {
                                                    colDef.foreignTable = columnDefinition2.Value.foreignTable;
                                                    colDef.foreignColumn = columnDefinition2.Value.foreignColumn;
                                                }
                                                else
                                                {
                                                    throw new Exception("Do not support merging 2 FKs yet!");
                                                }

                                                newDefinition.columnDefinitions[columnDefinition1.Key] = colDef;
                                            }
                                        }
                                    }
                                }

                                break;
                            }
                        }

                        if (!foundCol)
                        {
                            // Column was not found, add it
                            newDefinition.columnDefinitions.Add(columnDefinition2.Key, columnDefinition2.Value);
                        }
                    }

                    // Merge version definitions
                    foreach (var versionDefinition2 in secondFile.versionDefinitions)
                    {
                        var foundVersion = false;
                        foreach (var versionDefinition1 in firstFile.versionDefinitions)
                        {
                            if (foundVersion = VersionsOverlap(versionDefinition1, versionDefinition2))
                                break;
                        }

                        if (!foundVersion)
                        {
                            // TODO: Only for secondFile, not firstFile!
                            var mergedWithPreviousBuild = false;
                            var newVersions = newDefinition.versionDefinitions.ToList();

                            for (var i = 0; i < newVersions.Count; i++)
                            {
                                if (newVersions[i].definitions.SequenceEqual(versionDefinition2.definitions))
                                {
                                    Console.WriteLine("Looks like these two definition arrays are the same!");
                                    // Make list from current builds
                                    var curBuilds = newVersions[i].builds.ToList();
                                    curBuilds.AddRange(versionDefinition2.builds.ToList());

                                    // Make list from current build ranges
                                    var curBuildRanges = newVersions[i].buildRanges.ToList();
                                    curBuildRanges.AddRange(versionDefinition2.buildRanges.ToList());

                                    // Make list of current layouthashes
                                    var curLayouthashes = newVersions[i].layoutHashes.ToList();
                                    curLayouthashes.AddRange(versionDefinition2.layoutHashes.ToList());

                                    // Create temporary version object based of newVersion
                                    var tempVersion = newVersions[i];

                                    // Override builds with new list
                                    tempVersion.builds = curBuilds.Distinct().ToArray();

                                    // Override buildranges with new list
                                    tempVersion.buildRanges = curBuildRanges.Distinct().ToArray();

                                    // Override layoutHashes with new list
                                    tempVersion.layoutHashes = curLayouthashes.Distinct().ToArray();

                                    // Override newVersion with temporary version object
                                    newVersions[i] = tempVersion;
                                    mergedWithPreviousBuild = true;
                                }
                            }

                            if (!mergedWithPreviousBuild)
                            {
                                // Version was not found/merged, add it!
                                newVersions.Add(versionDefinition2);
                                numLayoutsAdded++;
                            }

                            newDefinition.versionDefinitions = newVersions.ToArray();
                        }
                        else
                        {
                            // Version exists, compare stuff and add build if needed, TODO make less bad
                            var newVersions = newDefinition.versionDefinitions.ToList();

                            for (var i = 0; i < newVersions.Count; i++)
                            {
                                if (VersionsOverlap(newVersions[i], versionDefinition2))
                                {
                                    // Make list from current builds
                                    var curBuilds = newVersions[i].builds.ToList();
                                    curBuilds.AddRange(versionDefinition2.builds.ToList());

                                    // Make list from current build ranges
                                    var curBuildRanges = newVersions[i].buildRanges.ToList();
                                    curBuildRanges.AddRange(versionDefinition2.buildRanges.ToList());

                                    // Make list of current layouthashes
                                    var curLayouthashes = newVersions[i].layoutHashes.ToList();
                                    curLayouthashes.AddRange(versionDefinition2.layoutHashes.ToList());

                                    // Create temporary version object based of newVersion
                                    var tempVersion = newVersions[i];

                                    // Override builds with new list
                                    tempVersion.builds = curBuilds.Distinct().ToArray();

                                    // Override buildranges with new list
                                    tempVersion.buildRanges = curBuildRanges.Distinct().ToArray();

                                    // Override layoutHashes with new list
                                    tempVersion.layoutHashes = curLayouthashes.Distinct().ToArray();

                                    for (var j = 0; j < versionDefinition2.definitions.Count(); j++)
                                    {
                                        // Merge signedness from second file if it is different from current
                                        if (versionDefinition2.definitions[j].isSigned != tempVersion.definitions[j].isSigned)
                                        {
                                            tempVersion.definitions[j].isSigned = versionDefinition2.definitions[j].isSigned;
                                        }
                                    }

                                    // Override newVersion with temporary version object
                                    newVersions[i] = tempVersion;
                                }
                            }

                            newDefinition.versionDefinitions = newVersions.ToArray();
                        }
                    }

                    newDefinitions.Add(Path.GetFileNameWithoutExtension(firstFileName), newDefinition);
                }
                else
                {
                    // Only 2nd dir has this file, use that
                    newDefinitions.Add(dbName, reader.Read(Path.Combine(secondDir, file)));
                }
            }

            foreach (var file in firstDirFiles)
            {
                if (!secondDirFilesLC.Contains(file.ToLower()))
                {
                    // Only 1st dir has this file, use that
                    newDefinitions.Add(Path.GetFileNameWithoutExtension(file), reader.Read(Path.Combine(firstDir, file)));
                }
            }

            var writer = new DBDWriter();
            foreach (var entry in newDefinitions)
            {
                var definitionCopy = entry.Value;
                var versionDefinitionCopy = definitionCopy.versionDefinitions.ToList();
                for (var i = 0; i < versionDefinitionCopy.Count(); i++)
                {
                    for (var j = 0; j < versionDefinitionCopy.Count(); j++)
                    {
                        if (i == j) continue; // Do not compare same entry

                        if (versionDefinitionCopy[i].definitions.SequenceEqual(versionDefinitionCopy[j].definitions))
                        {
                            if (versionDefinitionCopy[i].layoutHashes.Length > 0 && versionDefinitionCopy[j].layoutHashes.Length > 0 && !versionDefinitionCopy[i].layoutHashes.SequenceEqual(versionDefinitionCopy[j].layoutHashes))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine(Path.GetFileNameWithoutExtension(entry.Key) + " has 2 identical version definitions (" + (i + 1) + " and " + (j + 1) + ") but two different layouthashes, ignoring...");
                                Console.ResetColor();
                            }
                            else
                            {
                                // Make list from current builds
                                var curBuilds = versionDefinitionCopy[i].builds.ToList();
                                curBuilds.AddRange(versionDefinitionCopy[j].builds.ToList());

                                // Make list from current build ranges
                                var curBuildRanges = versionDefinitionCopy[i].buildRanges.ToList();
                                curBuildRanges.AddRange(versionDefinitionCopy[j].buildRanges.ToList());

                                // Make list of current layouthashes
                                var curLayouthashes = versionDefinitionCopy[i].layoutHashes.ToList();
                                curLayouthashes.AddRange(versionDefinitionCopy[j].layoutHashes.ToList());

                                // Create temporary version object based of newVersion
                                var tempVersion = versionDefinitionCopy[i];

                                // Override builds with new list
                                tempVersion.builds = curBuilds.Distinct().ToArray();

                                // Override buildranges with new list
                                tempVersion.buildRanges = curBuildRanges.Distinct().ToArray();

                                // Override layoutHashes with new list
                                tempVersion.layoutHashes = curLayouthashes.Distinct().ToArray();

                                // Override newVersion with temporary version object
                                versionDefinitionCopy[i] = tempVersion;
                                versionDefinitionCopy.RemoveAt(j);

                                definitionCopy.versionDefinitions = versionDefinitionCopy.ToArray();
                            }
                        }
                    }
                }

                // Run through column definitions to see if there's any unused columns 
                var columnDefinitionsCopy = definitionCopy.columnDefinitions.ToList();
                foreach (var columnDefinition in columnDefinitionsCopy)
                {
                    var columnUsed = false;
                    foreach (var versionDefinition in definitionCopy.versionDefinitions)
                    {
                        foreach (var definition in versionDefinition.definitions)
                        {
                            if (definition.name == columnDefinition.Key)
                            {
                                columnUsed = true;
                            }
                        }
                    }

                    if (!columnUsed)
                    {
                        definitionCopy.columnDefinitions.Remove(columnDefinition.Key);
                    }
                }

                writer.Save(definitionCopy, Path.Combine(targetDir, entry.Key + ".dbd"));
            }
            Console.WriteLine("Done, " + numLayoutsAdded + " new layouts added!");
            //Console.ReadLine();
        }

        private static bool VersionsOverlap(VersionDefinitions versionDefinition1, VersionDefinitions versionDefinition2)
        {
            // If layouthash was found, don't check builds
            if (versionDefinition1.layoutHashes.Intersect(versionDefinition2.layoutHashes).Any())
                return true;

            // Stop checking if build already exists
            if (versionDefinition1.builds.Intersect(versionDefinition2.builds).Any())
                return true;

            // Stop checking if ranges intersect
            if (versionDefinition1.buildRanges.Intersect(versionDefinition2.buildRanges).Any())
                return true;

            var v1Builds = versionDefinition1.builds.ToList();
            v1Builds.AddRange(versionDefinition1.buildRanges.Select(br => br.minBuild));
            v1Builds.AddRange(versionDefinition1.buildRanges.Select(br => br.maxBuild));

            if (v1Builds.FindIndex(build => versionDefinition2.buildRanges.Any(br => br.Contains(build))) > -1)
                return true;

            var v2Builds = versionDefinition2.builds.ToList();
            v2Builds.AddRange(versionDefinition2.buildRanges.Select(br => br.minBuild));
            v2Builds.AddRange(versionDefinition2.buildRanges.Select(br => br.maxBuild));

            if (v2Builds.FindIndex(build => versionDefinition1.buildRanges.Any(br => br.Contains(build))) > -1)
                return true;

            return false;
        }
    }
}
