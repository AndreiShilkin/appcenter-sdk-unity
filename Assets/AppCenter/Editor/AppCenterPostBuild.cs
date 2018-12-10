// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT license.

using Microsoft.AppCenter.Unity;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System;
#if UNITY_2018_1_OR_NEWER
using UnityEditor.Build.Reporting;
#endif
using UnityEditor.Build;
using UnityEditor;
using UnityEngine;

// Warning: Don't use #if #endif for conditional compilation here as Unity
// doesn't always set the flags early enough.

#if UNITY_2018_1_OR_NEWER
public class AppCenterPostBuild : IPostprocessBuildWithReport
#else
public class AppCenterPostBuild : IPostprocessBuild
#endif
{
    public int callbackOrder { get { return 0; } }

    private const string AppManifestFileName = "Package.appxmanifest";
    private const string CapabilitiesElement = "Capabilities";
    private const string CapabilityElement = "Capability";
    private const string CapabilityNameAttribute = "Name";
    private const string CapabilityNameAttributeValue = "internetClient";

#if UNITY_2018_1_OR_NEWER
    public void OnPostprocessBuild(BuildReport report)
    {
        OnPostprocessBuild(report.summary.platform, report.summary.outputPath);
    }
#endif

    public void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target == BuildTarget.WSAPlayer)
        {
            AddInternetClientCapability(pathToBuiltProject);
            AddHelperCodeToUWPProject(pathToBuiltProject);
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) != ScriptingImplementation.IL2CPP)
            {
                // If UWP with .NET scripting backend, need to add NuGet packages.
                var projectJson = pathToBuiltProject + "/" + PlayerSettings.productName + "/project.json";
                AddDependenciesToProjectJson(projectJson);

                var nuget = EditorApplication.applicationContentsPath + "/PlaybackEngines/MetroSupport/Tools/nuget.exe";
                ExecuteCommand(nuget, "restore \"" + projectJson + "\" -NonInteractive");
            }
            else
            {
                // Fix System.Diagnostics.Debug IL2CPP implementation.
                FixIl2CppLogging(pathToBuiltProject);
            }
        }
        if (target == BuildTarget.iOS &&
            PBXProjectWrapper.PBXProjectIsAvailable &&
            PlistDocumentWrapper.PlistDocumentIsAvailable)
        {
            var pbxProject = new PBXProjectWrapper(pathToBuiltProject);

            // Update project.
            OnPostprocessProject(pbxProject);
            pbxProject.WriteToFile();

            // Update Info.plist.
            var settings = AppCenterSettingsContext.SettingsInstance;
            var infoPath = pathToBuiltProject + "/Info.plist";
            var info = new PlistDocumentWrapper(infoPath);
            OnPostprocessInfo(info, settings);
            info.WriteToFile();

            // Update capabilities (if possible).
            if (ProjectCapabilityManagerWrapper.ProjectCapabilityManagerIsAvailable)
            {
                var capabilityManager = new ProjectCapabilityManagerWrapper(pbxProject.ProjectPath,
                                                                            PBXProjectWrapper.GetUnityTargetName());
                OnPostprocessCapabilities(capabilityManager, settings);
                capabilityManager.WriteToFile();
            }
        }

#if !UNITY_2018_2_OR_NEWER        
        if (target == BuildTarget.Android)
        {
            var settings = AppCenterSettingsContext.SettingsInstance;
            if (settings.UsePush && AppCenter.Push != null)
            {
                if (!EditorUserBuildSettings.exportAsGoogleAndroidProject)
                {
                    Debug.LogWarning("On Unity versions lower than 2018.1, you need to export the project in order for Push to work.");
                    return;
                }
                AndroidPostBuild.OnAndroidPostBuild(pathToBuiltProject);
            }
        }
#endif
    }

    #region UWP Methods
    public static void AddHelperCodeToUWPProject(string pathToBuiltProject)
    {
        var settings = AppCenterSettingsContext.SettingsInstance;
        if (!settings.UsePush || AppCenter.Push == null)
        {
            return;
        }

        // .NET, D3D
        if (EditorUserBuildSettings.wsaUWPBuildType == WSAUWPBuildType.D3D &&
            PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.WinRTDotNET)
        {
            var appFilePath = GetAppFilePath(pathToBuiltProject, "App.cs");
            var regexPattern = "private void ApplicationView_Activated \\( CoreApplicationView [a-zA-Z0-9_]*, IActivatedEventArgs args \\) {".Replace(" ", "[\\s]*");
            InjectCodeToFile(appFilePath, regexPattern, "d3ddotnet.txt");
        }
        // .NET, XAML
        else if (EditorUserBuildSettings.wsaUWPBuildType == WSAUWPBuildType.XAML &&
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.WinRTDotNET)
        {
            var appFilePath = GetAppFilePath(pathToBuiltProject, "App.xaml.cs");
            var regexPattern = "InitializeUnity\\(args.Arguments\\);";
            InjectCodeToFile(appFilePath, regexPattern, "xamldotnet.txt", false);
        }
        // IL2CPP, XAML
        else if (EditorUserBuildSettings.wsaUWPBuildType == WSAUWPBuildType.XAML &&
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.IL2CPP)
        {
            var appFilePath = GetAppFilePath(pathToBuiltProject, "App.xaml.cpp");
            var regexPattern = "InitializeUnity\\(e->Arguments\\);";
            InjectCodeToFile(appFilePath, regexPattern, "xamlil2cpp.txt", false);
        }
        // IL2CPP, D3D
        else if (EditorUserBuildSettings.wsaUWPBuildType == WSAUWPBuildType.D3D &&
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.WSA) == ScriptingImplementation.IL2CPP)
        {
            var appFilePath = GetAppFilePath(pathToBuiltProject, "App.cpp");
            var regexPattern = "void App::OnActivated\\(CoreApplicationView\\s*\\^ [a-zA-Z0-9_]+, IActivatedEventArgs\\s*\\^ [a-zA-Z0-9_]+\\) {".Replace(" ", "[\\s]*");
            InjectCodeToFile(appFilePath, regexPattern, "d3dil2cpp.txt");
        }
    }

    public static void InjectCodeToFile(string appFilePath, string searchRegex, string codeToInsertFileName, bool includeSearchText = true)
    {
        var appAdditionsFolder = AppCenterSettingsContext.AppCenterPath + "/AppCenter/Plugins/WSA/Push/AppAdditions";
        var codeToInsert = File.ReadAllText(Path.Combine(appAdditionsFolder, codeToInsertFileName));
        var commentText = "App Center Push code:";
        codeToInsert = "\n            // " + commentText + "\n" + codeToInsert;
        var fileText = File.ReadAllText(appFilePath);
        var regex = new Regex(searchRegex);
        var matches = regex.Match(fileText);
        if (matches.Success)
        {
            var codeToReplace = matches.ToString();
            if (!fileText.Contains(commentText))
            {
                if (includeSearchText)
                {
                    codeToInsert = codeToReplace + codeToInsert;
                }
                fileText = fileText.Replace(codeToReplace, codeToInsert);
            }
            File.WriteAllText(appFilePath, fileText);
        }
        else
        {
            // TODO Update documentation link
            Debug.LogError("Unable to automatically modify file '" + appFilePath + "'. For App Center Push to work properly, " +
                           "please follow troubleshooting instructions at https://docs.microsoft.com/en-us/mobile-center/sdk/troubleshooting/unity");
        }
    }

    public static void FixIl2CppLogging(string pathToBuiltProject)
    {
        var sourceDebuggerPath = "Assets\\AppCenter\\Plugins\\WSA\\IL2CPP\\Debugger.cpp.txt";
        var destDebuggerPath = Path.Combine(pathToBuiltProject,
            "Il2CppOutputProject\\IL2CPP\\libil2cpp\\icalls\\mscorlib\\System.Diagnostics\\Debugger.cpp");
        File.Copy(sourceDebuggerPath, destDebuggerPath, true);
    }

    public static string GetAppFilePath(string pathToBuiltProject, string filename)
    {
        var candidate = Path.Combine(pathToBuiltProject, PlayerSettings.WSA.tileShortName);
        candidate = Path.Combine(candidate, filename);
        return File.Exists(candidate) ? candidate : null;
    }

    public static void ProcessUwpIl2CppDependencies()
    {
        var binaries = AssetDatabase.FindAssets("*", new[] { AppCenterSettingsContext.AppCenterPath + "/AppCenter/Plugins/WSA/IL2CPP" });
        foreach (var guid in binaries)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer != null)
            {
                importer.SetPlatformData(BuildTarget.WSAPlayer, "SDK", "UWP");
                importer.SetPlatformData(BuildTarget.WSAPlayer, "ScriptingBackend", "Il2Cpp");
                importer.SaveAndReimport();
            }
        }
    }

    private static void AddDependenciesToProjectJson(string projectJsonPath)
    {
        if (!File.Exists(projectJsonPath))
        {
            Debug.LogWarning(projectJsonPath + " not found!");
            return;
        }
        var jsonString = File.ReadAllText(projectJsonPath);
        jsonString = AddDependencyToProjectJson(jsonString, "Microsoft.NETCore.UniversalWindowsPlatform", "5.2.2");
        jsonString = AddDependencyToProjectJson(jsonString, "Newtonsoft.Json", "10.0.3");
        jsonString = AddDependencyToProjectJson(jsonString, "sqlite-net-pcl", "1.3.1");
        jsonString = AddDependencyToProjectJson(jsonString, "System.Collections.NonGeneric", "4.0.1");
        File.WriteAllText(projectJsonPath, jsonString);
    }

    private static string AddDependencyToProjectJson(string projectJson, string packageId, string packageVersion)
    {
        const string quote = @"\" + "\"";
        var dependencyString = "\"" + packageId + "\": \"" + packageVersion + "\"";
        var pattern = quote + packageId + quote + @":[\s]+" + quote + "[^" + quote + "]*" + quote;
        var regex = new Regex(pattern);
        var match = regex.Match(projectJson);
        if (match.Success)
        {
            return projectJson.Replace(match.Value, dependencyString);
        }
        pattern = quote + "dependencies" + quote + @":[\s]+{";
        regex = new Regex(pattern);
        match = regex.Match(projectJson);
        var idx = projectJson.IndexOf(match.Value, StringComparison.Ordinal) + match.Value.Length;
        return projectJson.Insert(idx, "\n" + dependencyString + ",");
    }

    private static void ExecuteCommand(string command, string arguments, int timeout = 600)
    {
        try
        {
            var buildProcess = new System.Diagnostics.Process
            {
                StartInfo =
                {
                    FileName = command,
                    Arguments = arguments
                }
            };
            buildProcess.Start();
            buildProcess.WaitForExit(timeout * 1000);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void AddInternetClientCapability(string pathToBuiltProject)
    {
        /* Package.appxmanifest file example:
           <Package>
             <Capabilities>
               <Capability Name="internetClient" />
             </Capabilities>
           </Package> */

        var appManifests = Directory.GetFiles(pathToBuiltProject, AppManifestFileName, SearchOption.AllDirectories);
        if (appManifests.Length == 0)
        {
            Debug.LogWarning("Failed to add the `InternetClient` capability, file `" + AppManifestFileName + "` is not found");
            return;
        }
        else if (appManifests.Length > 1)
        {
            Debug.LogWarning("Failed to add the `InternetClient` capability, multiple `" + AppManifestFileName + "` files found");
            return;
        }

        var appManifestFilePath = appManifests[0];
        var xmlFile = XDocument.Load(appManifestFilePath);
        var defaultNamespace = xmlFile.Root.GetDefaultNamespace().NamespaceName;
        var capabilitiesElements = xmlFile.Root.Elements().Where(element => element.Name.LocalName == CapabilitiesElement).ToList();
        if (capabilitiesElements.Count > 1)
        {
            Debug.LogWarning("Failed to add the `InternetClient` capability, multiple `Capabilities` elements found inside `" + appManifestFilePath + "` file");
            return;
        }
        else if (capabilitiesElements.Count == 0)
        {
            xmlFile.Root.Add(new XElement(XName.Get(CapabilitiesElement, defaultNamespace), GetInternetClientCapabilityElement(defaultNamespace)));
        }
        else // capabilitiesElements.Count == 1
        {
            var capabilitiesElement = capabilitiesElements[0];
            foreach (var element in capabilitiesElement.Elements())
            {
                if (element.Name.LocalName == CapabilityElement &&
                    GetAttributeValue(element, CapabilityNameAttribute) == CapabilityNameAttributeValue)
                {
                    return;
                }
            }
            capabilitiesElement.Add(GetInternetClientCapabilityElement(defaultNamespace));
        }
        xmlFile.Save(appManifestFilePath);
    }

    private static XElement GetInternetClientCapabilityElement(string defaultNamespace)
    {
        return new XElement(XName.Get(CapabilityElement, defaultNamespace),
            new XAttribute(CapabilityNameAttribute, CapabilityNameAttributeValue));
    }

    internal static string GetAttributeValue(XElement element, string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        return attribute == null ? null : attribute.Value;
    }
    #endregion

    #region iOS Methods

    private static void OnPostprocessProject(PBXProjectWrapper project)
    {
        // Need to add "-lsqlite3" linker flag to "Other linker flags" due to
        // SQLite dependency.
        project.AddBuildProperty("OTHER_LDFLAGS", "-lsqlite3");
        project.AddBuildProperty("CLANG_ENABLE_MODULES", "YES");
    }

    private static void OnPostprocessInfo(PlistDocumentWrapper info, AppCenterSettings settings)
    {
        if (settings.UseDistribute && AppCenter.Distribute != null)
        {
            // Add App Center URL sceme.
            var root = info.GetRoot();
            var urlTypes = root.GetType().GetMethod("CreateArray").Invoke(root, new object[] { "CFBundleURLTypes" });
            var urlType = urlTypes.GetType().GetMethod("AddDict").Invoke(urlTypes, null);
            var setStringMethod = urlType.GetType().GetMethod("SetString");
            setStringMethod.Invoke(urlType, new object[] { "CFBundleTypeRole", "None" });
            setStringMethod.Invoke(urlType, new object[] { "CFBundleURLName", ApplicationIdHelper.GetApplicationId() });
            var urlSchemes = urlType.GetType().GetMethod("CreateArray").Invoke(urlType, new[] { "CFBundleURLSchemes" });
            urlSchemes.GetType().GetMethod("AddString").Invoke(urlSchemes, new[] { "appcenter-" + settings.iOSAppSecret });
        }
    }

    private static void OnPostprocessCapabilities(ProjectCapabilityManagerWrapper capabilityManager, AppCenterSettings settings)
    {
        if (settings.UsePush && AppCenter.Push != null)
        {
            capabilityManager.AddPushNotifications();
            capabilityManager.AddRemoteNotificationsToBackgroundModes();
        }
    }
    #endregion
}
