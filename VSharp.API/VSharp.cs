﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VSharp.Interpreter.IL;
using VSharp.Solver;

namespace VSharp
{
    /// <summary>
    /// Summary of V# test generation process.
    /// </summary>
    public sealed class Statistics
    {
        internal Statistics(TimeSpan time, DirectoryInfo outputDir, uint tests, uint errors, IEnumerable<string> iies)
        {
            TestGenerationTime = time;
            OutputDir = outputDir;
            TestsCount = tests;
            ErrorsCount = errors;
            IncompleteBranches = iies;
        }

        /// <summary>
        /// Overall time of test generation.
        /// </summary>
        public TimeSpan TestGenerationTime { get; }

        /// <summary>
        /// Directory where *.vst tests are placed
        /// </summary>
        public DirectoryInfo OutputDir { get; }

        /// <summary>
        /// The amount of generated unit tests.
        /// </summary>
        public uint TestsCount { get; }

        /// <summary>
        /// The amount of errors found.
        /// </summary>
        public uint ErrorsCount { get; }

        /// <summary>
        /// Some program branches might be failed to investigate. This enumerates the reasons of such failures.
        /// </summary>
        public IEnumerable<string> IncompleteBranches { get; }

        /// <summary>
        /// Writes textual summary of test generation process.
        /// </summary>
        /// <param name="writer">Output writer.</param>
        public void GenerateReport(TextWriter writer)
        {
            writer.WriteLine("Total time: {0:00}:{1:00}:{2:00}.{3}.", TestGenerationTime.Hours,
                TestGenerationTime.Minutes, TestGenerationTime.Seconds, TestGenerationTime.Milliseconds);
            var count = IncompleteBranches.Count();
            if (count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("{0} branch(es) with insufficient input information!", count);
                foreach (var message in IncompleteBranches)
                {
                    writer.WriteLine(message);
                }
            }
            writer.WriteLine("Test results written to {0}", OutputDir.FullName);
        }
    }

    public static class TestGenerator
    {
        private static Statistics StartExploration(List<MethodBase> methods, string resultsFolder)
        {
            var maxBound = 15u;
            var options =
                new SiliOptions(explorationMode.NewTestCoverageMode(coverageZone.MethodZone, searchMode.DFSMode),
                    executionMode.SymbolicMode, maxBound);
            SILI explorer = new SILI(options);
            UnitTests unitTests = new UnitTests(resultsFolder);
            Core.API.ConfigureSolver(SolverPool.mkSolver());
            foreach (var method in methods)
            {
                if (method == method.Module.Assembly.EntryPoint)
                {
                    explorer.InterpretEntryPoint(method, unitTests.GenerateTest, unitTests.GenerateError, _ => { },
                        e => throw e);
                }
                else
                {
                    explorer.InterpretIsolated(method, unitTests.GenerateTest, unitTests.GenerateError, _ => { },
                        e => throw e);
                }
            }

            var statistics = new Statistics(explorer.Statistics.CurrentExplorationTime, unitTests.TestDirectory,
                unitTests.UnitTestsCount, unitTests.ErrorsCount,
                explorer.Statistics.IncompleteStates.Select(e => e.iie.Value.Message).Distinct());
            unitTests.WriteReport(explorer.Statistics.PrintStatistics);
            return statistics;
        }

        private static bool Reproduce(DirectoryInfo testDir)
        {
            return TestRunner.TestRunner.ReproduceTests(testDir);
        }

        /// <summary>
        /// Generates test coverage for specified method.
        /// </summary>
        /// <param name="method">Type to be covered with tests.</param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>Summary of tests generation process.</returns>
        public static Statistics Cover(MethodBase method, string outputDirectory = "")
        {
            List<MethodBase> methods = new List<MethodBase> {method};
            return StartExploration(methods, outputDirectory);
        }

        /// <summary>
        /// Generates test coverage for all public methods of specified type.
        /// </summary>
        /// <param name="type">Type to be covered with tests.</param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>Summary of tests generation process.</returns>
        /// <exception cref="ArgumentException">Thrown if specified class does not contain public methods.</exception>
        public static Statistics Cover(Type type, string outputDirectory = "")
        {
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                        BindingFlags.DeclaredOnly;
            List<MethodBase> methods = new List<MethodBase>();
            foreach (var m in type.GetMethods(bindingFlags))
            {
                methods.Add(m);
            }

            if (methods.Count == 0)
            {
                throw new ArgumentException("I've not found any public method of class " + type.FullName);
            }

            return StartExploration(methods, outputDirectory);
        }

        /// <summary>
        /// Generates test coverage for the specified assembly.
        /// </summary>
        /// <param name="assembly">Assembly to be covered with tests.</param>
        /// <param name="allPublicMethods">If true, then the engine will try to generate tests for all public methods.
        /// If false, the engine will try to generate tests starting from the entry point of assembly (assembly should contain Main method).
        /// </param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>Summary of tests generation process.</returns>
        /// <exception cref="ArgumentException">If allPublicMethods is true, then thrown if no public methods found in assembly.
        /// Otherwise thrown if assembly does not contain entry point.
        /// </exception>
        public static Statistics Cover(Assembly assembly, bool allPublicMethods, string outputDirectory = "")
        {
            List<MethodBase> methods;
            if (allPublicMethods)
            {
                var entryPoint = assembly.EntryPoint;
                if (entryPoint == null)
                {
                    throw new ArgumentException("I've not found entry point in assembly");
                }
                methods = new List<MethodBase> { entryPoint };
            }
            else
            {
                BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                            BindingFlags.DeclaredOnly;
                methods = new List<MethodBase>();
                foreach (var t in assembly.GetTypes())
                {
                    if (t.IsPublic)
                    {
                        foreach (var m in t.GetMethods(bindingFlags))
                        {
                            methods.Add(m);
                        }
                    }
                }

                if (methods.Count == 0)
                {
                    throw new ArgumentException("I've not found any public method in assembly");
                }
            }

            return StartExploration(methods, outputDirectory);
        }

        /// <summary>
        /// Generates test coverage for the specified assembly and runs all tests.
        /// </summary>
        /// <param name="method">Type to be covered with tests.</param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>True if all generated tests have passed.</returns>
        public static bool CoverAndRun(MethodBase method, string outputDirectory = "")
        {
            var stats = Cover(method, outputDirectory);
            return Reproduce(stats.OutputDir);
        }

        /// <summary>
        /// Generates test coverage for the specified assembly and runs all tests.
        /// </summary>
        /// <param name="type">Type to be covered with tests.</param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>True if all generated tests have passed.</returns>
        /// <exception cref="ArgumentException">Thrown if specified class does not contain public methods.</exception>
        public static bool CoverAndRun(Type type, string outputDirectory = "")
        {
            var stats = Cover(type, outputDirectory);
            return Reproduce(stats.OutputDir);
        }

        /// <summary>
        /// Generates test coverage for the specified assembly and runs all tests.
        /// </summary>
        /// <param name="assembly">Assembly to be covered with tests.</param>
        /// <param name="allPublicMethods">If true, then the engine will try to generate tests for all public methods.
        /// If false, the engine will try to generate tests starting from the entry point of assembly (assembly should contain Main method).
        /// </param>
        /// <param name="outputDirectory">Directory to place generated *.vst tests. If null or empty, process working directory is used.</param>
        /// <returns>True if all generated tests have passed.</returns>
        /// <exception cref="ArgumentException">If allPublicMethods is true, then thrown if no public methods found in assembly.
        /// Otherwise thrown if assembly does not contain entry point.
        /// </exception>
        public static bool CoverAndRun(Assembly assembly, bool allPublicMethods, string outputDirectory = "")
        {
            var stats = Cover(assembly, allPublicMethods, outputDirectory);
            return Reproduce(stats.OutputDir);
        }

    }

}