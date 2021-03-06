﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.Agg.PlatformAbstract;
using MatterHackers.MatterControl.SettingsManagement;
using MatterHackers.PolygonMesh;
using MatterHackers.PolygonMesh.Processors;

namespace MatterHackers.MatterControl.SlicerConfiguration
{
    public class SlicingQueue
    {
        static Thread slicePartThread = null;
        static List<PrintItemWrapper> listOfSlicingItems = new List<PrintItemWrapper>();
        static bool haltSlicingThread = false;

        static List<SliceEngineInfo> availableSliceEngines;
        static public List<SliceEngineInfo> AvailableSliceEngines
        {
            get
            {
                if (availableSliceEngines == null)
                {
                    availableSliceEngines = new List<SliceEngineInfo>();
                    Slic3rInfo slic3rEngineInfo = new Slic3rInfo();
                    if (slic3rEngineInfo.Exists())
                    {
                        availableSliceEngines.Add(slic3rEngineInfo);
                    }
                    CuraEngineInfo curaEngineInfo = new CuraEngineInfo();
                    if (curaEngineInfo.Exists())
                    {
                        availableSliceEngines.Add(curaEngineInfo);
                    }
                    MatterSliceInfo matterSliceEngineInfo = new MatterSliceInfo();
                    if (matterSliceEngineInfo.Exists())
                    {
                        availableSliceEngines.Add(matterSliceEngineInfo);
                    }
                }
                return availableSliceEngines;
            }
        }

        static private SliceEngineInfo getSliceEngineInfoByType(ActivePrinterProfile.SlicingEngineTypes engineType)
        {
            foreach (SliceEngineInfo info in AvailableSliceEngines)
            {
                if (info.GetSliceEngineType() == engineType)
                {
                    return info;
                }
            }
            return null;
        }
				

        SlicingQueue()
        {
            if (slicePartThread == null)
            {
                slicePartThread = new Thread(CreateSlicedPartsThread);
                slicePartThread.Name = "slicePartThread";
                slicePartThread.IsBackground = true;
                slicePartThread.Start();
            }
        }

        static SlicingQueue instance;
        static public SlicingQueue Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new SlicingQueue();
                }
                return instance;
            }
        }

        public void QueuePartForSlicing(PrintItemWrapper itemToQueue)
        {
            itemToQueue.DoneSlicing = false;
			string preparingToSliceModelTxt = LocalizedString.Get("Preparing to slice model");
			string peparingToSliceModelFull = string.Format ("{0}...", preparingToSliceModelTxt);
			itemToQueue.OnSlicingOutputMessage(new StringEventArgs(peparingToSliceModelFull));
            using (TimedLock.Lock(listOfSlicingItems, "QueuePartForSlicing"))
            {
                //Add to thumbnail generation queue
                listOfSlicingItems.Add(itemToQueue);
            }
        }

        public void ShutDownSlicingThread()
        {
            haltSlicingThread = true;
        }

		static string macQuotes(string textLine)
		{
			if (textLine.StartsWith ("\"") && textLine.EndsWith ("\"")) {
				return textLine;
			} else {
				return "\"" + textLine.Replace ("\"", "\\\"") + "\"";
			}
		}

        static string getSlicerFullPath()
        {
            SliceEngineInfo info = getSliceEngineInfoByType(ActivePrinterProfile.Instance.ActiveSliceEngineType);
            if (info != null)
            {
                return info.GetEnginePath();
            }
            else
            {
                //throw new Exception("Slice engine is unavailable");
				return null;
            }

        }

        public static string[] GetStlFileLocations(string fileToSlice)
        {
            int extruderCount = ActiveSliceSettings.Instance.ExtruderCount;
            switch (Path.GetExtension(fileToSlice).ToUpper())
            {
                case ".STL":
                case ".GCODE":
                    return new string[] { fileToSlice };

                case ".AMF":
                    List<MeshGroup> meshGroups = MeshFileIo.Load(fileToSlice);
                    List<MeshGroup> extruderMeshGroups = new List<MeshGroup>();
                    for(int extruderIndex = 0; extruderIndex < extruderCount; extruderIndex++)
                    {
                        extruderMeshGroups.Add(new MeshGroup());
                    }
                    int maxExtruderIndex = 0;
                    foreach (MeshGroup meshGroup in meshGroups)
                    {
                        foreach (Mesh mesh in meshGroup.Meshes)
                        {
                            MeshMaterialData material = MeshMaterialData.Get(mesh);
                            int extruderIndex = Math.Max(0, material.MaterialIndex-1);
                            maxExtruderIndex = Math.Max(maxExtruderIndex, extruderIndex);
                            if(extruderIndex >= extruderCount)
                            {
                                extruderMeshGroups[0].Meshes.Add(mesh);
                            }
                            else
                            {
                                extruderMeshGroups[extruderIndex].Meshes.Add(mesh);
                            }
                        }
                    }

                    List<string> extruderFilesToSlice = new List<string>();
                    for (int i = 0; i < extruderMeshGroups.Count; i++ )
                    {
                        MeshGroup meshGroup = extruderMeshGroups[i];
                        List<int> materialsToInclude = new List<int>();
                        materialsToInclude.Add(i+1);
                        if (i == 0)
                        {
                            for (int j = extruderCount+1; j < maxExtruderIndex + 2; j++)
                            {
                                materialsToInclude.Add(j);
                            }
                        }

                        extruderFilesToSlice.Add(SaveAndGetFilenameForMaterial(meshGroup, materialsToInclude));
                    }
                    return extruderFilesToSlice.ToArray();

                default:
                    throw new NotImplementedException();
            }
        }

        private static string SaveAndGetFilenameForMaterial(MeshGroup extruderMeshGroup, List<int> materialIndexsToSaveInThisSTL)
        {
            string fileName = Path.ChangeExtension(Path.GetRandomFileName(), ".stl");
            string applicationUserDataPath = ApplicationDataStorage.Instance.ApplicationUserDataPath;
            string folderToSaveStlsTo = Path.Combine(applicationUserDataPath, "data", "temp", "amf_to_stl");
            if (!Directory.Exists(folderToSaveStlsTo))
            {
                Directory.CreateDirectory(folderToSaveStlsTo);
            }
            MeshOutputSettings settings = new MeshOutputSettings();
            settings.MaterialIndexsToSave = materialIndexsToSaveInThisSTL;
            string extruder1StlFileToSlice = Path.Combine(folderToSaveStlsTo, fileName);
            MeshFileIo.Save(extruderMeshGroup, extruder1StlFileToSlice, settings);
            return extruder1StlFileToSlice;
        }

        public static bool runInProcess = false;
        static Process slicerProcess = null;
        static void CreateSlicedPartsThread()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            while (!haltSlicingThread)
            {
                if (PrinterConnectionAndCommunication.Instance.ActivePrintItem != null && listOfSlicingItems.Count > 0)
                {
                    PrintItemWrapper itemToSlice = listOfSlicingItems[0];
                    string[] stlFileLocations = GetStlFileLocations(itemToSlice.FileLocation);
                    string fileToSlice = stlFileLocations[0];
                    // check that the STL file is currently on disk
                    if (File.Exists(fileToSlice))
                    {
                        itemToSlice.CurrentlySlicing = true;

                        string currentConfigurationFileAndPath = Path.Combine(ApplicationDataStorage.Instance.GCodeOutputPath, "config_" + ActiveSliceSettings.Instance.GetHashCode().ToString() + ".ini");
                        ActiveSliceSettings.Instance.GenerateConfigFile(currentConfigurationFileAndPath);

                        string gcodePathAndFileName = itemToSlice.GetGCodePathAndFileName();
                        bool gcodeFileIsComplete = itemToSlice.IsGCodeFileComplete(gcodePathAndFileName);

                        if (!File.Exists(gcodePathAndFileName) || !gcodeFileIsComplete)
                        {
                            string commandArgs = "";

                            switch (ActivePrinterProfile.Instance.ActiveSliceEngineType)
                            {
                                case ActivePrinterProfile.SlicingEngineTypes.Slic3r:
                                    commandArgs = "--load \"" + currentConfigurationFileAndPath + "\" --output \"" + gcodePathAndFileName + "\" \"" + fileToSlice + "\"";
                                    break;

                                case ActivePrinterProfile.SlicingEngineTypes.CuraEngine:
                                    commandArgs = "-v -o \"" + gcodePathAndFileName + "\" " + EngineMappingCura.GetCuraCommandLineSettings() + " \"" + fileToSlice + "\"";
                                    break;

                                case ActivePrinterProfile.SlicingEngineTypes.MatterSlice:
                                    {
                                        EngineMappingsMatterSlice.WriteMatterSliceSettingsFile(currentConfigurationFileAndPath);
                                        commandArgs = "-v -o \"" + gcodePathAndFileName + "\" -c \"" + currentConfigurationFileAndPath + "\"";
                                        foreach (string filename in stlFileLocations)
                                        {
                                            commandArgs = commandArgs + " \"" + filename + "\"";
                                        }
                                    }
                                    break;
                            }

                            
							if (OsInformation.OperatingSystem == OSType.Android ||
								((OsInformation.OperatingSystem == OSType.Mac || runInProcess)
									&& ActivePrinterProfile.Instance.ActiveSliceEngineType == ActivePrinterProfile.SlicingEngineTypes.MatterSlice))
                            {
                                itemCurrentlySlicing = itemToSlice;
                                MatterHackers.MatterSlice.LogOutput.GetLogWrites += SendProgressToItem;
                                MatterSlice.MatterSlice.ProcessArgs(commandArgs);
                                MatterHackers.MatterSlice.LogOutput.GetLogWrites -= SendProgressToItem;
                                itemCurrentlySlicing = null;
                            }
                            else
                            {
                                slicerProcess = new Process();
                                slicerProcess.StartInfo.Arguments = commandArgs;
                                string slicerFullPath = getSlicerFullPath();

                                slicerProcess.StartInfo.CreateNoWindow = true;
                                slicerProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                                slicerProcess.StartInfo.RedirectStandardError = true;
                                slicerProcess.StartInfo.RedirectStandardOutput = true;

                                slicerProcess.StartInfo.FileName = slicerFullPath;
                                slicerProcess.StartInfo.UseShellExecute = false;

                                slicerProcess.OutputDataReceived += (sender, args) =>
                                {
                                    if (args.Data != null)
                                    {
                                        string message = args.Data;
                                        message = message.Replace("=>", "").Trim();
                                        if (message.Contains(".gcode"))
                                        {
                                            message = "Saving intermediate file";
                                        }
                                        message += "...";
                                        UiThread.RunOnIdle((state) =>
                                        {
                                            itemToSlice.OnSlicingOutputMessage(new StringEventArgs(message));
                                        });
                                    }
                                };

                                slicerProcess.Start();
                                slicerProcess.BeginOutputReadLine();
                                string stdError = slicerProcess.StandardError.ReadToEnd();

                                slicerProcess.WaitForExit();
                                using (TimedLock.Lock(slicerProcess, "SlicingProcess"))
                                {
                                    slicerProcess = null;
                                }
                            }
                        }

                        try
                        {
                            if (File.Exists(gcodePathAndFileName)
                                && File.Exists(currentConfigurationFileAndPath))
                            {
                                using (StreamWriter gcodeWirter = File.AppendText(gcodePathAndFileName))
                                {
                                    string oemName = "MatterControl";
                                    if (OemSettings.Instance.WindowTitleExtra != null && OemSettings.Instance.WindowTitleExtra.Trim().Length > 0)
                                    {
                                        oemName = oemName + " - {0}".FormatWith(OemSettings.Instance.WindowTitleExtra);
                                    }

                                    gcodeWirter.WriteLine("; {0} Version {1} Build {2} : GCode settings used".FormatWith(oemName, VersionInfo.Instance.ReleaseVersion, VersionInfo.Instance.BuildVersion));
                                    gcodeWirter.WriteLine("; Date {0} Time {1}:{2:00}".FormatWith(DateTime.Now.Date, DateTime.Now.Hour, DateTime.Now.Minute));

                                    foreach (string line in File.ReadLines(currentConfigurationFileAndPath))
                                    {
                                        gcodeWirter.WriteLine("; {0}".FormatWith(line));
                                    }
                                }
                            }
                        }
                        catch(Exception)
                        {
                        }
                    }

                    UiThread.RunOnIdle((state) =>
                    {
                        itemToSlice.CurrentlySlicing = false;
                        itemToSlice.DoneSlicing = true;
                    });

                    using (TimedLock.Lock(listOfSlicingItems, "CreateSlicedPartsThread()"))
                    {
                        listOfSlicingItems.RemoveAt(0);
                    }
                }

                Thread.Sleep(100);
            }
        }

        static PrintItemWrapper itemCurrentlySlicing;
        static void SendProgressToItem(object sender, EventArgs args)
        {
            string message = sender as string;
            if (message != null)
            {
                message = message.Replace("=>", "").Trim();
                if (message.Contains(".gcode"))
                {
                    message = "Saving intermediate file";
                }
                message += "...";
                UiThread.RunOnIdle((state) =>
                {
                    if (itemCurrentlySlicing != null)
                    {
                        itemCurrentlySlicing.OnSlicingOutputMessage(new StringEventArgs(message));
                    }
                });
            }
        }

        internal void CancelCurrentSlicing()
        {
            if (slicerProcess != null)
            {
                using (TimedLock.Lock(slicerProcess, "SlicingProcess"))
                {
                    if (slicerProcess != null && !slicerProcess.HasExited)
                    {
                        slicerProcess.Kill();
                    }
                }
            }
        }
    }
}
