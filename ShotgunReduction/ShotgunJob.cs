﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Hadoop.MapReduce;
using Microsoft.Hadoop;
using Microsoft.Hadoop.MapReduce.HdfsExtras.Hdfs;
using System.Globalization;



namespace ShotgunReduction
{
    class ShotgunJob : HadoopJob<ShotgunMapper, ShotgunReducer>
    //shotgun step: produce many clustering models
    //this job uses a fast variation of leader clustering to produce many "guesses" at possible clusterings of the data set
    //these guesses are the result of distance thresholds based off of mean and stdev produced in previous steps.
    //this will be improved by adding code to adjust each centroid as points are to assigned to it (centroid is currently just the first point that fell in that group rather than average of points in the group)
    {
        public override HadoopJobConfiguration Configure(ExecutorContext context)
        {
            var config = new HadoopJobConfiguration();
            config.InputPath = "input/clustering";
            config.OutputFolder = "output/clustering/shotgun";

            return config;
        }

    }
    //7400 points per cluster, 15 clusters in sample data set
    //110k points total

    class ShotgunMapper : MapperBase
    {
        Random randGen = new Random();
        public override void Map(string inputLine, MapperContext context)
        {


            //might want to reexamine the way this works.
            //might be better to generate the random number outside of the for loop.
            //emitting once for each of the 21 distance thresholds
            for (int i = 0; i < 21; i++)
            {
                //tagging this with the random map group (0-9), and also the distance group (0-20)
                context.EmitKeyValue(randGen.Next(0, 10).ToString() + ", " + i.ToString(), inputLine);
            }


        }

    }

    class ShotgunReducer : ReducerCombinerBase
    {
        public double mean;
        public double stDev;
        double zScore;
        public override void Initialize(ReducerCombinerContext context)
        {

            //try to read stats computed in previous step
            //if stats don't exist, throw an exception
            String[] stats = HdfsFile.ReadAllLines("/user/Aegon/output/clustering/finalStats/part-00000");
            context.Log("Contents of read all lines: " + stats[0]);

            //file.
            if (stats.Length <= 0)
            {
                throw new Exception("Cluster Statistics not defined");

            }
            else
            {
                string[] line1 = stats[0].Split('\t');
                string[] line2 = stats[1].Split('\t');

                string readMean = line1[1];
                string readStDev = line2[1];

                mean = double.Parse(readStDev);
                context.Log("mean parsed");
                stDev = double.Parse(readStDev);
                context.Log("stdev parsed");
            }
        }

        public bool zTest(double zScore, double distance)
        {
            double z = (distance - mean) / stDev;
            if (z <= zScore)
            {
                return true;
            }
            return false;
        }
        public override void Reduce(string key, IEnumerable<string> values, ReducerCombinerContext context)
        {
            //this is the fast leader clustering
            List<double> zList = new List<double>();
            //21 of these z's total
            zList.Add(-3.49);
            zList.Add(-1.65);
            zList.Add(-1.28);
            zList.Add(-1.04);
            zList.Add(-.84);
            zList.Add(-.67);
            zList.Add(-.52);
            zList.Add(-.39);
            zList.Add(-.25);
            zList.Add(-.13);
            zList.Add(0.0);
            zList.Add(.13);
            zList.Add(.25);
            zList.Add(.39);
            zList.Add(.52);
            zList.Add(.67);
            zList.Add(.84);
            zList.Add(1.04);
            zList.Add(1.28);
            zList.Add(1.65);
            zList.Add(3.49);
            /*for each point
             *  find the closest centroid
             *  test the distance against the zscore
             *  either assign point to that centroid of create new centroid
             *  repeat
             */
            var keySplit = key.Split(',');
            var zIndex = int.Parse(keySplit[1]);
            zScore = zList[zIndex];
            bool writeOut = true;
            List<ClusterPoint> centroids = new List<ClusterPoint>();
            int count = 0;

            foreach (string value in values)
            {
                string[] split = value.Split(',');
                double xVal = double.Parse(split[0]);
                double yVal = double.Parse(split[1]);
                ClusterPoint current = new ClusterPoint(xVal, yVal);

                if (centroids.Count() != 0)
                {
                    double minDistance = double.MaxValue;
                    ClusterPoint minPoint = null;

                    foreach (ClusterPoint centroid in centroids)
                    {
                        double distance = centroid.SquaredDistance(current);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            minPoint = centroid;

                        }


                    }
                    //if not in any current clusters, create a new one
                    if (!zTest(zScore, minDistance))
                    {
                        centroids.Add(current);

                    }
                    //if the point falls in a cluster, adjust that centroid
                    else
                    {
                        minPoint.AdjustCentroid(current);
                    }
                }
                else
                {
                    //empty list, add first centroid
                    centroids.Add(current);
                }
                //break if too many clusters
                //disregard result
                count++;
                if (count > 300 && centroids.Count >= count / 2)
                {
                    writeOut = false;
                    break;
                }
            }


            //write out the centroids here
            //currently only writing count
            //eventually will write more information as well
            if (writeOut)
            {

                context.EmitKeyValue("#centroids:", centroids.Count().ToString());
            }

        }
    }
}