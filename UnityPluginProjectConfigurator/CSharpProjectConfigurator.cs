﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;

namespace ShuHai.UnityPluginProjectConfigurator
{
    using AssemblyReference = Configs.UnityManagedPluginProject.AssemblyReference;
    using VersionToInfo = Dictionary<UnityVersion, Configs.UnityManagedPluginProject.VersionInfo>;
    using StringToVersionInfo = Dictionary<string, Configs.UnityManagedPluginProject.VersionInfo>;
    using XmlPropertyGroup = ProjectPropertyGroupElement;
    using XmlItemGroup = ProjectItemGroupElement;
    using ConditionNames = VSProject.ConditionNames;
    using Condition = VSProject.Condition;
    using Conditions = VSProject.Conditions;

    public static class CSharpProjectConfigurator
    {
        #region Parameter

        public sealed class UnityPluginParameter
        {
            public bool ForEditor;

            public VersionToInfo Versions;
        }

        public static UnityPluginParameter ParseUnityPluginParameter(Configs.UnityPlugins configs, string projectPath)
        {
            Ensure.Argument.NotNull(configs, nameof(configs));
            Ensure.Argument.NotNullOrEmpty(projectPath, nameof(projectPath));
            return ParseUnityPluginParameter(configs, configs.ManagedProjects[projectPath]);
        }

        public static UnityPluginParameter ParseUnityPluginParameter(
            Configs.UnityPlugins configs, Configs.UnityManagedPluginProject projectConfig)
        {
            Ensure.Argument.NotNull(configs, nameof(configs));
            Ensure.Argument.NotNull(projectConfig, nameof(projectConfig));

            StringToVersionInfo versions = null;
            if (projectConfig.ForMultipleVersions)
            {
                versions = projectConfig.Versions ?? new StringToVersionInfo();
                foreach (var kvp in configs.DefaultVersions)
                {
                    var ver = kvp.Key;
                    var info = kvp.Value;
                    if (!versions.ContainsKey(ver))
                        versions.Add(ver, info);
                }
            }

            var parameter = new UnityPluginParameter
            {
                ForEditor = projectConfig.ForEditor,
                Versions = versions?.ToDictionary(p => UnityVersion.Parse(p.Key), p => p.Value)
            };
            return parameter;
        }

        #endregion Parameter

        #region Configure

        public static void SetupUnityPluginProject(VSProject project, UnityPluginParameter parameter)
        {
            Ensure.Argument.NotNull(project, nameof(project));
            Ensure.Argument.NotNull(parameter, nameof(parameter));

            var versions = parameter.Versions;
            if (versions == null)
                return;

            project.RemovePropertyGroups(project.ParseConditionalConfigurationPropertyGroups((string)null));
            project.RemoveItemGroups(project.ParseConditionalConfigurationItemGroups((string)null));

            var propertyGroupAnchor = project.DefaultPropertyGroup;
            var itemGroupAnchor = project.DefaultReferenceGroup;
            foreach (var kvp in versions)
            {
                var ver = kvp.Key;
                var info = kvp.Value;

                foreach (var type in EnumTraits<ProjectConfigurationType>.Values)
                {
                    SetupConfigurationGroupForUnity(
                        propertyGroupAnchor = project.CreatePropertyGroupAfter(propertyGroupAnchor),
                        type, parameter.ForEditor, ver);

                    var references = info.AssemblyReferences;
                    if (!CollectionUtil.IsNullOrEmpty(references))
                    {
                        SetupReferenceGroupForUnity(
                            itemGroupAnchor = project.CreateItemGroupAfter(itemGroupAnchor),
                            type, parameter.ForEditor, ver, references);
                    }
                }
            }
        }

        #region Configurations

        private static void SetupConfigurationGroupForUnity(
            XmlPropertyGroup group, ProjectConfigurationType type, bool forEditor, UnityVersion version)
        {
            var condition = CreateCondition(type, version);
            group.Condition = condition.ToString();

            bool isDebug = type == ProjectConfigurationType.Debug;
            if (isDebug)
                group.SetProperty("DebugSymbols", "true");

            group.SetProperty("DebugType", isDebug ? "full" : "pdbonly");
            group.SetProperty("Optimize", (!isDebug).ToString().ToLower());
            group.SetProperty("OutputPath", $@"bin\{condition[ConditionNames.Configuration]}");
            group.SetProperty("DefineConstants", GetDefineConstantsForUnity(isDebug, forEditor, version));
            group.SetProperty("ErrorReport", "prompt");
            group.SetProperty("WarningLevel", "4");
        }

        private static string GetDefineConstantsForUnity(bool isDebug, bool forUnityEditor, UnityVersion version)
        {
            var directives = new List<string> { "TRACE" };
            if (isDebug)
                directives.Add("DEBUG");
            if (forUnityEditor)
                directives.Add("UNITY_EDITOR");

            if (version != null)
            {
                if (version.Major == null)
                    throw new ArgumentException("Major version number is required.", nameof(version));

                // Unity-5.3 is the minimum unity version that defines X_X_OR_NEWER constants.
                var directiveVer = HistoricalUnityVersions.Unity_5_3;
                while (directiveVer != null && directiveVer <= version)
                {
                    directives.Add($"UNITY_{directiveVer.Cycle}_{directiveVer.Major.Value}_OR_NEWER");
                    if (!HistoricalUnityVersions.NextMajorVersion(directiveVer, out directiveVer))
                        break;
                }
                directives.Add($"UNITY_{version.Cycle}_{version.Major}");
                directives.Add($"UNITY_{version.Cycle}");
            }

            return string.Join(";", directives);
        }

        #endregion Configurations

        #region References

        private static void SetupReferenceGroupForUnity(XmlItemGroup group,
            ProjectConfigurationType type, bool forEditor, UnityVersion version,
            IEnumerable<AssemblyReference> references)
        {
            var condition = CreateCondition(type, version);
            group.Condition = condition.ToString();

            foreach (var asmRef in references)
            {
                var env = UnityEnvironmentTypesConverter.FromStrings(asmRef.Environments);
                if (env == UnityEnvironmentTypes.Editor && !forEditor)
                    continue;

                var path = asmRef.Path;
                var include = asmRef.Name ?? Path.GetFileNameWithoutExtension(path);
                var item = group.AddItem("Reference", include);
                item.AddMetadata("HintPath", path);
            }
        }

        #endregion References

        private static Conditions CreateCondition(ProjectConfigurationType type, UnityVersion version)
        {
            return new Conditions(new[]
            {
                new Condition(ConditionNames.Configuration, $"{type}-{version.ToString(true)}"),
                new Condition(ConditionNames.Platform, "AnyCPU")
            });
        }

        #endregion Configure
    }
}