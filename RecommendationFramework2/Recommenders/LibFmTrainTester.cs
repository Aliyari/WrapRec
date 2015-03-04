﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyMediaLite.Data;
using System.IO;
using System.Diagnostics;
using LinqLib.Sequence;

namespace WrapRec.Recommenders
{
    public class LibFmTrainTester : ITrainTester<ItemRating>
    {
        // Mapping _usersItemsMap;
        
        // path to a folder to save temprorary converted files
        string _dataStorePath;

        // libFm parameters
        public string LibFmPath { get; set; }
        public double LearningRate { get; set; }
        public int Iterations { get; set; }
        public string Dimensions { get; set; }
        public string Regularization { get; set; }
        public FmLearnigAlgorithm LearningAlgorithm { get; set; }
        public string TrainFile { get; set; }
        public string TestFile { get; set; }
        public bool CreateBinaryFiles { get; set; }
        public double RMSE { get; private set; }

        string _experimentId;

        public LibFmFeatureBuilder FeatureBuilder { get; set; }

        public LibFmTrainTester(string experimentId = "", LibFmFeatureBuilder featureBuilder = null, string dataStorePath = "",
            string libFmPath = "LibFm.Net.64.exe",
            double learningRate = 0.05, 
            int numIterations = 30, 
            string dimensions = "1,1,8", 
            FmLearnigAlgorithm alg = FmLearnigAlgorithm.ALS,
            string regularization = "0,0,0.1",
            string trainFile = "",
            string testFile = "")
        {
            _experimentId = experimentId;

            //_usersItemsMap = new Mapping();
            _dataStorePath = !String.IsNullOrEmpty(dataStorePath) && dataStorePath.Last() != '\\' ? dataStorePath + "\\" : dataStorePath;

            if (featureBuilder == null)
                FeatureBuilder = new LibFmFeatureBuilder();
            else
                FeatureBuilder = featureBuilder;
            
            // default properties
            LibFmPath = libFmPath;
            LearningRate = learningRate;
            Iterations = numIterations;
            Dimensions = dimensions;
            LearningAlgorithm = alg;
            Regularization = regularization;
            TrainFile = trainFile;
            TestFile = testFile;

            CreateBinaryFiles = false;
        }

        public void TrainAndTest(IEnumerable<ItemRating> trainSet, IEnumerable<ItemRating> testSet)
        {
            string expIdExtension = string.IsNullOrEmpty(_experimentId) ? "" : "." + _experimentId;

            if (TrainFile == "")
            {
                TrainFile = _dataStorePath + "train.libfm" + expIdExtension;
                TestFile = _dataStorePath + "test.libfm" + expIdExtension;
            }

            string testOutput = _dataStorePath + "test.out" + expIdExtension;

            // converting train and test data to libFm files becuase libfm.exe only get file names as input
            SaveLibFmFile(trainSet, TrainFile);
            SaveLibFmFile(testSet, TestFile);

            if (CreateBinaryFiles)
            {
                ConvertAndTransform(TrainFile);
                ConvertAndTransform(TestFile);
            }

            // initialize the process
            var libFm = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = LibFmPath,
                    Arguments = BuildArguments(TrainFile, TestFile, testOutput),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            string libFMTrainRMSE = "", libFMTestRMSE = "";
            double lowestRMSE = double.MaxValue;
            int iter = 0, lowestIteration = 0;

            libFm.OutputDataReceived += (p, dataLine) =>
            {
                var data = dataLine.Data;

                if (data != null && (data.StartsWith("Loading") || data.StartsWith("#")))
                {
                    Log.Logger.Trace(dataLine.Data);

                    if (data.StartsWith("#Iter"))
                    {
                        libFMTrainRMSE = data.Substring(data.IndexOf("Train") + 6, 6);
                        libFMTestRMSE = data.Substring(data.IndexOf("Test") + 5);

                        double current = double.Parse(libFMTestRMSE);

                        if (current < lowestRMSE)
                        {
                            lowestRMSE = current;
                            lowestIteration = iter;
                        }

                        iter++;
                    }
                }
            };

            var startTime = DateTime.Now;
            
            libFm.Start();
            libFm.BeginOutputReadLine();
            libFm.WaitForExit();

            var duration = (int)DateTime.Now.Subtract(startTime).TotalMilliseconds;

            Log.Logger.Info("Lowest RMSE on test set reported by LibFm is: {0:0.0000} at iteration {1}", lowestRMSE, lowestIteration);
            Log.Logger.Info("LibFm pure train and test time: {0:N0} ms", duration);

            RMSE = lowestRMSE;
            UpdateTestSet(testSet, testOutput);

            // write actual ratings in the test set
            File.WriteAllLines(_dataStorePath + "test.act", testSet.Select(ir => ir.Rating.ToString()).ToList());
        }

        private void SaveLibFmFile(IEnumerable<ItemRating> dataset, string fileName)
        {
            var output = dataset.Select(ir => FeatureBuilder.GetLibFmFeatureVector(ir)).ToList();
            File.WriteAllLines(fileName, output);
        }

        public void ConvertAndTransform(string libfmFile)
        {
            string convertArgs = string.Format("--ifile {0} --ofilex {0}.bin.x --ofiley {0}.bin.y", libfmFile);
            string transposeArgs = string.Format("--ifile {0}.bin.x --ofile {0}.bin.xt", libfmFile);

            var convert = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "convert.exe",
                    Arguments = convertArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var transpose = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "transpose.exe",
                    Arguments = transposeArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
           
            Log.Logger.Trace("Converting LibFm file to binary format. Input file: {0}", libfmFile);
            convert.Start();
            convert.WaitForExit();
            
            Log.Logger.Trace("Transposing LibFm binary file {0}.bin.x", libfmFile);
            transpose.Start();
            transpose.WaitForExit();

            Log.Logger.Trace("Transposing finished.");
        }


        private string BuildArguments(string trainFile, string testFile, string testOutput)
        {
            if (CreateBinaryFiles)
            {
                trainFile += ".bin";
                testFile += ".bin";
            }

            return String.Format("-task r -train {0} -test {1} -method {2} -iter {3} -dim {4} -learn_rate {5} -out {6} -regular {7}",
                trainFile, testFile, LearningAlgorithm.ToString().ToLower(), Iterations, Dimensions, LearningRate, testOutput, Regularization);
        }

        private void UpdateTestSet(IEnumerable<ItemRating> testSet, string testOutput)
        {
            var predictedRatings = File.ReadAllLines(testOutput).ToList();

            // it is important that the order of test samples and predicted ratings in the output file remains the same
            // the testSet should already be a list to make sure that the updates on items applies on the original set
            int i = 0;
            foreach (var itemRating in testSet)
            {
                itemRating.PredictedRating = float.Parse(predictedRatings[i++]);
            }
        }

    }

    public enum FmLearnigAlgorithm
    {
        MCMC,
        SGD,
        SGDA,
        ALS
    }
}
