﻿using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.TestAdapter.Config;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Microsoft.PythonTools.TestAdapter.Pytest {
    static class PyTestDiscoveryReader {

        public static IEnumerable<TestCase> ParseDiscovery(IList<PytestDiscoveryResults> results, ITestCaseDiscoverySink discoverySink, PythonProjectSettings settings, IMessageLogger logger) {
            if (!results.Any())
                return null;

            var testcases = new List<TestCase>();

            logger.SendMessage(TestMessageLevel.Informational, "Discovered the following tests:");

            foreach (PytestDiscoveryResults result in results) {
                Dictionary<string, PytestParent> parentMap = BuildParentMap(result);
               
                foreach (var t in result.Tests) {
                    var sourceAndLineNum = t.Source.Replace(".\\", "");
                    String[] sourceParts = sourceAndLineNum.Split(':');
                    Debug.Assert(sourceParts.Length == 2);

                    if (sourceParts.Length == 2 &&
                        Int32.TryParse(sourceParts[1], out int line) &&
                        !String.IsNullOrWhiteSpace(t.Name) &&
                        !String.IsNullOrWhiteSpace(t.Id)) {

                        var sourceNoLineNumbers = sourceParts[0];
                        
                        //bschnurr todo: fix codepath for files outside of project
                        var fullSourcePathNormalized = Path.Combine(result.Root, sourceNoLineNumbers).ToLower();

                        Uri executorURI = settings.IsWorkspace ? PythonConstants.WorkspaceExecutorUri : PythonConstants.ExecutorUri;
                        var fullyQualifiedName = CreateFullyQualifiedTestNameFromId(t.Id);
                        var tc = new TestCase(fullyQualifiedName, executorURI, fullSourcePathNormalized) {
                            DisplayName = t.Name,
                            LineNumber = line,
                            CodeFilePath = fullSourcePathNormalized
                        };

                        logger.SendMessage(TestMessageLevel.Informational, $"{tc.DisplayName} Source:{tc.Source} Line:{tc.LineNumber}");
                        
                        tc.SetPropertyValue(Constants.PytestIdProperty, t.Id);
                        tc.SetPropertyValue(Constants.PyTestXmlClassNameProperty, CreateXmlClassName(t, parentMap));
                        tc.SetPropertyValue(Constants.PytestTestExecutionPathPropertery, GetAbsoluteTestExecutionPath(fullSourcePathNormalized, t.Id));
                        tc.SetPropertyValue(Constants.IsWorkspaceProperty, settings.IsWorkspace);
              
                        discoverySink?.SendTestCase(tc);
                        
                        testcases.Add(tc);

                    } else {
                        Debug.WriteLine("Testcase parse failed:\n {0}".FormatInvariant(t.Id));
                    }
                }
            }

            return testcases;
        }

        private static Dictionary<string, PytestParent> BuildParentMap(PytestDiscoveryResults result) {
            var parentMap = new Dictionary<string, PytestParent>();
            result.Parents.ForEach(p => parentMap[p.Id] = p);
            return parentMap;
        }

        /// <summary>
        /// Creates a classname that matches the junit testresult generated one so that we can match testresults with testcases
        /// Note if a function doesn't have a class, its classname appears to be the filename without an extension
        /// </summary>
        /// <param name="t"></param>
        /// <param name="parentMap"></param>
        /// <returns></returns>
        public static string CreateXmlClassName(PytestTest t, Dictionary<string, PytestParent> parentMap) {
            var parentList = new List<string>();
            var currId = t.Parentid;
            while (parentMap.TryGetValue(currId, out PytestParent parent)) {
                // class names for functions dont append the direct parent 
                if (String.Compare(parent.Kind, "function", StringComparison.OrdinalIgnoreCase) != 0) {
                    parentList.Add(Path.GetFileNameWithoutExtension(parent.Name));
                }
                currId = parent.Parentid;
            }
            parentList.Reverse();

            var xmlClassName = String.Join(".", parentList);
            return xmlClassName;
        }

        public static string CreateFullyQualifiedTestNameFromId(string pytestId) {
            var fullyQualifiedName = pytestId.Replace(".\\", "");
            String[] parts = fullyQualifiedName.Split(new string[] { "::" }, StringSplitOptions.None); 

            // set classname as filename, without extension for test functions outside of classes,
            // so test explorer doesn't use .py as the classname
            if (parts.Length == 2) {
                var className = Path.GetFileNameWithoutExtension(parts[0]);
                return $"{parts[0]}::{className}::{parts[1]}";
            }
            return fullyQualifiedName;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="absoluteFilePath"></param>
        /// <param name="pytestId"></param>
        /// <returns></returns>
        public static string GetAbsoluteTestExecutionPath(string absoluteFilePath, string pytestId) {
            var filename = Path.GetFileName(absoluteFilePath);
            var executionTestPath = "";
            var index = pytestId.LastIndexOf(filename);
            if (index != -1) {
                //join full codefilepath and pytestId but remove overlapping directories or filename
                var functionName = pytestId.Substring(index + filename.Length);
                executionTestPath = absoluteFilePath + functionName;
            } else {
                executionTestPath = Path.Combine(Path.GetDirectoryName(absoluteFilePath), pytestId.TrimStart('.'));
            }
            return executionTestPath;
        }
    }
}
