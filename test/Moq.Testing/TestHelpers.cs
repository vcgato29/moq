﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using Xunit;

static partial class TestHelpers
{
    public static (AdhocWorkspace workspace, Project project) CreateWorkspaceAndProject(string language, string assemblyName = "Code")
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = CreateProjectInfo(language, assemblyName);
        var project = workspace.AddProject(projectInfo);

        return (workspace, project);
    }

    public static ProjectInfo CreateProjectInfo(string language, string assemblyName)
    {
        var suffix = language == LanguageNames.CSharp ? "CS" : "VB";
        var options = language == LanguageNames.CSharp ?
                (CompilationOptions)new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default) :
                (CompilationOptions)new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On, assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
        var parse = language == LanguageNames.CSharp ?
                (ParseOptions)new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest) :
                (ParseOptions)new VisualBasicParseOptions(Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.Latest);

        //The location of the .NET assemblies
        var frameworkPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var netstandardPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".nuget\packages\NETStandard.Library\2.0.0\build\netstandard2.0\ref");
        if (!Directory.Exists(netstandardPath))
            netstandardPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"dotnet\sdk\NuGetFallbackFolder\netstandard.library\2.0.0\build\netstandard2.0\ref");
        if (!Directory.Exists(netstandardPath))
            netstandardPath = @"C:\Program Files\dotnet\sdk\NuGetFallbackFolder\netstandard.library\2.0.0\build\netstandard2.0\ref";

        if (!Directory.Exists(netstandardPath))
            throw new InvalidOperationException("Failed to find location of .NETStandard 2.0 reference assemblies");

        var referencePaths = Directory.EnumerateFiles(netstandardPath, "*.dll")
            .Concat(ReferencePaths.Paths)
            .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
            .Distinct(FileNameEqualityComparer.Default);

        var projectId = ProjectId.CreateNewId();
        var stuntApi = language == LanguageNames.CSharp ?
            new FileInfo(@"..\..\..\..\src\Stunts\Stunts\contentFiles\cs\netstandard1.3\Stunt.cs").FullName :
            new FileInfo(@"..\..\..\..\src\Stunts\Stunts\contentFiles\vb\netstandard1.3\Stunt.vb").FullName;

        Assert.True(File.Exists(stuntApi));

        return ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),  
            assemblyName + "." + suffix,
            assemblyName + "." + suffix,
            language,
            compilationOptions: options,
            parseOptions: parse,
            metadataReferences: referencePaths
                .Select(path => MetadataReference.CreateFromFile(path)),
            documents: new[] 
            {
                DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId, Path.GetFileName(stuntApi)),
                    Path.GetFileName(stuntApi), 
                    loader: new FileTextLoader(stuntApi, Encoding.Default),
                    filePath: stuntApi)
            });
    }

    public static CancellationToken TimeoutToken(int seconds)
        => Debugger.IsAttached ?
            new CancellationTokenSource().Token :
            new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;

    public static Assembly Emit(this Compilation compilation)
    {
        using (var stream = new MemoryStream())
        {
            var result = compilation.Emit(stream);
            result.AssertSuccess();

            stream.Seek(0, SeekOrigin.Begin);
            return Assembly.Load(stream.ToArray());
        }
    }

    public static void AssertSuccess(this EmitResult result)
    {
        if (!result.Success)
        {
            Assert.False(true,
                "Emit failed:\r\n" +
                Environment.NewLine +
                string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        }
    }

    public class FileNameEqualityComparer : IEqualityComparer<string>
    {
        public static IEqualityComparer<string> Default { get; } = new FileNameEqualityComparer();

        FileNameEqualityComparer() { }

        public bool Equals(string x, string y) => Path.GetFileName(x).Equals(Path.GetFileName(y));

        public int GetHashCode(string obj) => Path.GetFileName(obj).GetHashCode();
    }
}