using AbcSharp.ABC;
using AbcSharp.SWF;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using MahApps.Metro.Controls;

namespace LESs
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow

    {
        private const string INTENDED_VERSION = "0.0.1.148";

        private readonly BackgroundWorker _worker = new BackgroundWorker();
        private ErrorLevel _errorLevel = ErrorLevel.NoError;
        private Dictionary<CheckBox, LessMod> _lessMods = new Dictionary<CheckBox, LessMod>();
        private Stopwatch timer;
        private ServerType type;

        private string _modsDirectory = "modifications";

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called when the program encounters any exception. Displays a message box to the user alerting them to the error.
        /// </summary>
        private void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            Exception Error = e.Exception;
            MessageBox.Show(Error.Message + Environment.NewLine + Error.StackTrace + Environment.NewLine);
        }
        /// <summary>
        /// Called on the initial loading of Lol Enhancement Suite.
        /// </summary>
        private void MainGrid_Loaded(object sender, RoutedEventArgs e)
        {
            LeagueVersionLabel.Content = "Version: " + INTENDED_VERSION;
            MessageBox.Show("League of Legends Mod Patcher updated for: " + INTENDED_VERSION + Environment.NewLine + Environment.NewLine + "Powered by RATzone Community", "League of Legends Mod Patcher");

            //Create the debug log. Delete it if it already exists.
            if (File.Exists("debug.log"))
                File.Delete("debug.log");

            File.Create("debug.log").Dispose();

            //Set the events for the worker when the patching starts.
            _worker.DoWork += worker_DoWork;
            _worker.RunWorkerCompleted += worker_RunWorkerCompleted;

            //Enable exception logging if the debugger ISNT attached.
            if (!Debugger.IsAttached)
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

            //try to find the mods in the base directory of the solution when the debugger is attached
            if (Debugger.IsAttached && !Directory.Exists(_modsDirectory) && Directory.Exists("../../../modifications"))
            {
                _modsDirectory = "../../../modifications";
            }

            //Make sure that the mods exist in the directory. Warn the user if they dont.
            if (!Directory.Exists(_modsDirectory))
                MessageBox.Show("Missing modifications directory. Ensure that all files were extracted properly.", "LoLMP Missing files");

            var modList = Directory.GetDirectories(_modsDirectory);

            //Add each mod to the mod list.
            foreach (string mod in modList)
            {
                string modJsonFile = Path.Combine(mod, "mod.json");

                if (File.Exists(modJsonFile))
                {
                    JavaScriptSerializer s = new JavaScriptSerializer();
                    LessMod lessMod = s.Deserialize<LessMod>(File.ReadAllText(modJsonFile));
                    lessMod.Directory = mod;

                    CheckBox Check = new CheckBox();
                    Check.IsChecked = !lessMod.DisabledByDefault;
                    Check.Content = lessMod.Name;
                    ModsListBox.Items.Add(Check);

                    _lessMods.Add(Check, lessMod);
                }
            }
        }

        /// <summary>
        /// Change the label & description when the mod is hovered over.
        /// </summary>
        private void ModsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CheckBox box = (CheckBox)ModsListBox.SelectedItem;

            if (box == null)
                return;

            LessMod lessMod = _lessMods[box];

            ModNameLabel.Content = lessMod.Name;
            ModDescriptionBox.Text = lessMod.Description;
            //see if our mod has an author and display it
            if (!string.IsNullOrEmpty(lessMod.Author))
                ModAuthorLabel.Content = "Mod Author: " + lessMod.Author;
            else
                ModAuthorLabel.Content = "Mod Author: -Unknown-";

            if (!string.IsNullOrEmpty(lessMod.Version) && lessMod.Version != "0.0.1.148")
            {
                this.ModVersionLabel.Content = "Mod Version: " + lessMod.Version + " (OUTDATED)";
                return;
            }
            if (!string.IsNullOrEmpty(lessMod.Version) && lessMod.Version == "0.0.1.148")
            {
                this.ModVersionLabel.Content = "Mod Version: " + lessMod.Version + " (UPDATED)";
                return;
            }
            this.ModVersionLabel.Content = "Mod Version: Not Specified";
        }

        /// <summary>
        /// Called when the user looks for their League of Legends installation
        /// </summary>
        private void FindButton_Click(object sender, RoutedEventArgs e)
        {
            //Disable patching if the user selects another league installation.
            PatchButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;

            //Create a file dialog for the user to locate their league of legends installation.
            OpenFileDialog findLeagueDialog = new OpenFileDialog();

            //If they don't have League of Legends in the default path, look for Garena.
            if (!Directory.Exists(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Riot Games", "League of Legends")))
                findLeagueDialog.InitialDirectory = Path.GetFullPath(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Program Files (x86)", "GarenaLoL", "GameData", "Apps", "LoL"));
            else
                findLeagueDialog.InitialDirectory = Path.GetFullPath(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Riot Games", "League of Legends"));

            findLeagueDialog.DefaultExt = ".exe";
            findLeagueDialog.Filter = "League of Legends Launcher|lol.launcher*.exe|Garena Launcher|lol.exe";

            bool? foundLeague = findLeagueDialog.ShowDialog();

            if (foundLeague == true)
            {
                LogToFile("Selected Location", findLeagueDialog.FileName);

                //Remove the executable from the location
                Uri Location = new Uri(findLeagueDialog.FileName);
                string SelectedLocation = Location.LocalPath.Replace(Location.Segments.Last(), string.Empty);

                //Get the executable name to check for Garena
                string LastSegment = Location.Segments.Last();

                if (!LastSegment.StartsWith("lol.launcher"))
                {
                    type = ServerType.GARENA;
                    RemoveButton.IsEnabled = true;
                    PatchButton.IsEnabled = true;
                    LocationTextbox.Text = Path.Combine(SelectedLocation, "Air");
                }
                else
                {
                    //Check each RADS installation to find the latest installation
                    string radsLocation = Path.Combine(SelectedLocation, "RADS", "projects", "lol_air_client", "releases");

                    LogToFile("RADS Location", radsLocation);

                    var versionDirectories = Directory.GetDirectories(radsLocation);
                    string finalDirectory = "";
                    string version = "";
                    uint versionCompare = 0;
                    foreach (string x in versionDirectories)
                    {
                        string compare1 = x.Substring(x.LastIndexOfAny(new char[] { '\\', '/' }) + 1);

                        string[] versionParts = compare1.Split(new char[] { '.' });

                        if (!compare1.Contains(".") || versionParts.Length != 4)
                            continue;

                        uint CompareVersion;
                        try //versions have the format "x.x.x.x" where every x can be a value between 0 and 255 
                        {
                            CompareVersion = Convert.ToUInt32(versionParts[0]) << 24 | Convert.ToUInt32(versionParts[1]) << 16 | Convert.ToUInt32(versionParts[2]) << 8 | Convert.ToUInt32(versionParts[3]);
                        }
                        catch (FormatException) //can happen for directories like "0.0.0.asasd"
                        {
                            continue;
                        }

                        if (CompareVersion > versionCompare)
                        {
                            versionCompare = CompareVersion;
                            version = x.Replace(radsLocation + "\\", "");
                            finalDirectory = x;
                        }

                        LogToFile("Version Found", CompareVersion.ToString());
                    }

                    if (version != INTENDED_VERSION)
                    {
                        string Message = string.Format("This version of League of Legends Mod Patcher is intended for {0}. Your current version of League of Legends is {1}. Continue? This could harm your installation.", INTENDED_VERSION, version);
                        MessageBoxResult versionMismatchResult = MessageBox.Show(Message, "Invalid Version", MessageBoxButton.YesNo);
                        if (versionMismatchResult == MessageBoxResult.No)
                            this.Close();
                    }

                    type = ServerType.NORMAL;
                    PatchButton.IsEnabled = true;
                    RemoveButton.IsEnabled = true;

                    LocationTextbox.Text = Path.Combine(finalDirectory, "deploy");
                }

                Directory.CreateDirectory(Path.Combine(LocationTextbox.Text, "LoLMPBackup"));
                Directory.CreateDirectory(Path.Combine(LocationTextbox.Text, "LoLMPBackup", INTENDED_VERSION));
            }
        }

        /// <summary>
        /// Called when the user tries to patch their League of Legends installation.
        /// </summary>
        private void PatchButton_Click(object sender, RoutedEventArgs e)
        {
            PatchButton.IsEnabled = false;
            LogToFile("Patch", "Patching started");
            _worker.RunWorkerAsync();
        }

        /// <summary>
        /// Called when the user wants to remove LESs from their League of Legends installation.
        /// </summary>
        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            string CurrentLocation = Path.Combine(LocationTextbox.Text, "LoLMPBackup");
            string[] targetVersions = Directory.GetDirectories(CurrentLocation);
            for (int i = 0; i < targetVersions.Length; i++)
            {
                targetVersions[i] = targetVersions[i].Remove(0, CurrentLocation.Length).Replace("\\", "").Replace("/", "");
            }

            RemovePopup popup = new RemovePopup(type, targetVersions, LocationTextbox.Text);
            popup.Show();
        }

        /// <summary>
        /// Gets all the mods and patches them.
        /// </summary>
        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            _errorLevel = ErrorLevel.NoError;

            //Gets the list of mods
            ItemCollection modCollection = null;
            string lolLocation = null;

            Dispatcher.Invoke(DispatcherPriority.Input, new ThreadStart(() =>
            {
                modCollection = ModsListBox.Items;
                lolLocation = LocationTextbox.Text;
            }));

            SetStatusLabelAsync("Gathering mods...");
            //Gets the list of mods that have been checked.
            List<LessMod> modsToPatch = new List<LessMod>();
            foreach (var x in modCollection)
            {
                CheckBox box = (CheckBox)x;
                bool isBoxChecked = false;
                Dispatcher.Invoke(DispatcherPriority.Input, new ThreadStart(() =>
                {
                    isBoxChecked = box.IsChecked ?? false;
                }));

                if (isBoxChecked)
                {
                    modsToPatch.Add(_lessMods[box]);
                }
            }

            Dictionary<string, SwfFile> swfs = new Dictionary<string, SwfFile>();
            timer = Stopwatch.StartNew();
            foreach (var lessMod in modsToPatch)
            {
                Debug.Assert(lessMod.Patches.Length > 0);
                SetStatusLabelAsync("Patching mod: " + lessMod.Name);
                foreach (var patch in lessMod.Patches)
                {
                    if (!swfs.ContainsKey(patch.Swf))
                    {
                        string fullPath = Path.Combine(lolLocation, patch.Swf);

                        //Backup the SWF
                        string CurrentLocation = "";
                        string[] FileLocation = patch.Swf.Split('/', '\\');
                        foreach (string s in FileLocation.Take(FileLocation.Length - 1))
                        {
                            CurrentLocation = Path.Combine(CurrentLocation, s);
                            if (!Directory.Exists(Path.Combine(lolLocation, "LoLMPBackup", INTENDED_VERSION, CurrentLocation)))
                            {
                                Directory.CreateDirectory(Path.Combine(lolLocation, "LoLMPBackup", INTENDED_VERSION, CurrentLocation));
                            }
                        }
                        if (!File.Exists(Path.Combine(lolLocation, "LoLMPBackup", INTENDED_VERSION, patch.Swf)))
                        {
                            File.Copy(Path.Combine(lolLocation, patch.Swf), Path.Combine(lolLocation, "LoLMPBackup", INTENDED_VERSION, patch.Swf));
                        }

                        swfs.Add(patch.Swf, SwfFile.ReadFile(fullPath));
                    }

                    SwfFile swf = swfs[patch.Swf];
                    List<DoAbcTag> tags = swf.GetDoAbcTags();
                    bool classFound = false;
                    foreach (var tag in tags)
                    {
                        //check if this tag contains our script
                        ScriptInfo si = tag.GetScriptByClassName(patch.Class);

                        //check next tag if it doesn't
                        if (si == null)
                            continue;

                        ClassInfo cls = si.GetClassByClassName(patch.Class);
                        classFound = true;

                        Assembler asm;
                        switch (patch.Action)
                        {
                            case "replace_trait": //replace trait (method)
                                asm = new Assembler(File.ReadAllText(Path.Combine(lessMod.Directory, patch.Code)));
                                TraitInfo newTrait = asm.Assemble() as TraitInfo;

                                int traitIndex = cls.Instance.GetTraitIndexByTypeAndName(newTrait.Type, newTrait.Name.Name);
                                bool classTrait = false;
                                if (traitIndex < 0)
                                {
                                    traitIndex = cls.GetTraitIndexByTypeAndName(newTrait.Type, newTrait.Name.Name);
                                    classTrait = true;
                                }
                                if (traitIndex < 0)
                                {
                                    throw new TraitNotFoundException(String.Format("Can't find trait \"{0}\" in class \"{1}\"", newTrait.Name.Name, patch.Class));
                                }

                                if (classTrait)
                                {
                                    cls.Traits[traitIndex] = newTrait;
                                }
                                else
                                {
                                    cls.Instance.Traits[traitIndex] = newTrait;
                                }
                                break;
                            case "replace_cinit"://replace class constructor
                                asm = new Assembler(File.ReadAllText(Path.Combine(lessMod.Directory, patch.Code)));
                                cls.ClassInit = asm.Assemble() as MethodInfo;
                                break;
                            case "replace_iinit"://replace instance constructor
                                asm = new Assembler(File.ReadAllText(Path.Combine(lessMod.Directory, patch.Code)));
                                cls.Instance.InstanceInit = asm.Assemble() as MethodInfo;
                                break;
                            case "add_class_trait": //add new class trait (method)
                                asm = new Assembler(File.ReadAllText(Path.Combine(lessMod.Directory, patch.Code)));
                                newTrait = asm.Assemble() as TraitInfo;
                                traitIndex = cls.GetTraitIndexByTypeAndName(newTrait.Type, newTrait.Name.Name);
                                if (traitIndex < 0)
                                {
                                    cls.Traits.Add(newTrait);
                                }
                                else
                                {
                                    cls.Traits[traitIndex] = newTrait;
                                }
                                break;
                            case "add_instance_trait": //add new instance trait (method)
                                asm = new Assembler(File.ReadAllText(Path.Combine(lessMod.Directory, patch.Code)));
                                newTrait = asm.Assemble() as TraitInfo;
                                traitIndex = cls.Instance.GetTraitIndexByTypeAndName(newTrait.Type, newTrait.Name.Name);
                                if (traitIndex < 0)
                                {
                                    cls.Instance.Traits.Add(newTrait);
                                }
                                else
                                {
                                    cls.Instance.Traits[traitIndex] = newTrait;
                                }
                                break;
                            case "remove_class_trait":
                                throw new NotImplementedException();
                            case "remove_instance_trait":
                                throw new NotImplementedException();
                            default:
                                throw new NotSupportedException("Unknown Action \"" + patch.Action + "\" in mod " + lessMod.Name);
                        }
                    }

                    if (!classFound)
                    {
                        _errorLevel = ErrorLevel.UnableToPatch;
                        throw new ClassNotFoundException(string.Format("Class {0} not found in file {1}", patch.Class, patch.Swf));
                    }
                }
            }
            //return;

            foreach (var patchedSwf in swfs)
            {
                try
                {
                    SetStatusLabelAsync("Applying mods: " + patchedSwf.Key);
                    string swfLoc = Path.Combine(lolLocation, patchedSwf.Key);
                    SwfFile.WriteFile(patchedSwf.Value, swfLoc);
                }
                catch
                {
                    _errorLevel = ErrorLevel.GoodJobYourInstallationIsProbablyCorruptedNow;
                    if (Debugger.IsAttached)
                        throw;
                }
            }
            timer.Stop();
        }

        /// <summary>
        /// Called once LESs has been successfully patched into the client.
        /// </summary>
        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            switch (_errorLevel)
            {
                case ErrorLevel.NoError:
                    StatusLabel.Content = "Done patching!";
                    MessageBox.Show("LoLMP has been successfully patched into League of Legends!\n(In " + timer.ElapsedMilliseconds + "ms)");
                    break;
                case ErrorLevel.UnableToPatch:
                    SetStatusLabelAsync("[Error] Please check debug.log for more information.");
                    MessageBox.Show("LoLMP encountered errors during patching. No mods have been applied.");
                    break;
                case ErrorLevel.GoodJobYourInstallationIsProbablyCorruptedNow:
                    SetStatusLabelAsync("[Critical Error] Please check debug.log for more information.");
                    MessageBox.Show("LoLMP encountered errors during patching.\nIt is possible your client is corrupted.\nPlease repair before trying again.");
                    break;
            }
            PatchButton.IsEnabled = true;
        }

        /// <summary>
        /// Outputs debug information to a file.
        /// </summary>
        private void LogToFile(string subject, string message)
        {
            File.AppendAllText("debug.log", string.Format("[{0}] {1}{2}", subject, message, Environment.NewLine));
        }

        /// <summary>
        /// Sets the text of the status label
        /// </summary>
        private void SetStatusLabelAsync(string text)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new ThreadStart(() =>
            {
                StatusLabel.Content = text;
            }));
        }
    }
}