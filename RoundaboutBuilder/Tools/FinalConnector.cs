﻿using ColossalFramework;
using ColossalFramework.Math;
using Provisional.Actions;
using SharedEnvironment.Public.Actions;
using System;
using System.Collections.Generic;
using UnityEngine;

/* By Strad, 01/2019 */

/* Version RELEASE 1.2.0+ */

namespace RoundaboutBuilder.Tools
{
    /* This class takes the nodes obtained from EdgeIntersections2 and connects them together to create the final roundabout. */

    /* This is all such garbage with old algortithms mixing with new and other stuff... Not the nicest piece of code. */

    public class FinalConnector
    {
        private static readonly int DISTANCE_MIN = 20; // Min distance from other nodes when inserting controlling node

        private List<VectorNodeStruct> intersections;
        private bool leftHandTraffic;
        private Ellipse ellipse;
        private NetInfo centerNodeNetInfo;
        private ActionGroup m_group;
        private double m_maxAngDistance;

        // Time to time something goes wrong. Let's make sure that we don't get stuck in infinite recursion.
        // Didn't happen to me since I degbugged it, but one never knows for sure.
        private int pleasenoinfiniterecursion;

        public FinalConnector(NetInfo centerNodeNetInfo, EdgeIntersections2 edgeIntersections, Ellipse ellipse, bool insertControllingVertices)
        {
            intersections = edgeIntersections?.Intersections ?? new List<VectorNodeStruct>();
            m_group = edgeIntersections?.TmpeActionGroup();

            this.ellipse = ellipse;
            pleasenoinfiniterecursion = 0;
            this.centerNodeNetInfo = centerNodeNetInfo;
            leftHandTraffic = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic ==
                                    SimulationMetaData.MetaBool.True;

            // We ensure that the segments are not too long. For circles only (with ellipses it would be more difficult)
            m_maxAngDistance = Math.Min(Math.PI * 25 / ellipse.RadiusMain , Math.PI/2 + 0.1d);

            bool isCircle = ellipse.IsCircle();
            if (!isCircle && insertControllingVertices)
            {
                /* See doc in the method below */
                InsertIntermediateNodes();
            }

            /* If the list of edge nodes is empty, we add one default intersection. */
            if (isCircle && intersections.Count == 0)
            {
                Vector3 defaultIntersection = new Vector3(ellipse.RadiusMain, 0, 0) + ellipse.Center;
                ushort newNodeId = NetAccess.CreateNode(centerNodeNetInfo, defaultIntersection);
                intersections.Add(new VectorNodeStruct(newNodeId));
            }

            int count = intersections.Count;
            foreach (VectorNodeStruct item in intersections)
            {
                item.angle = Ellipse.VectorsAngle(item.vector - ellipse.Center);
            }

            /* We sort the nodes according to their angles */
            intersections.Sort();

            /* Goes over all the nodes and conntets each of them to the angulary closest neighbour. (In a given direction) */
            
            for (int i = 0; i < count; i++)
            {
                VectorNodeStruct prevNode = intersections[i];
                if (isCircle)
                    prevNode = CheckAngularDistance(intersections[i], intersections[(i + 1) % count]);
                ConnectNodes(intersections[(i + 1) % count], prevNode);
            }

            if(m_group != null)
            {
                ModThreading.Timer(m_group);
            }
        }

        /* For circles only. */
        private VectorNodeStruct CheckAngularDistance(VectorNodeStruct p1, VectorNodeStruct p2)
        {
            VectorNodeStruct prevNode = p1;
            double angDif = NormalizeAngle(prevNode.angle - p2.angle);
            if (p1 == p2)
                angDif = 2 * Math.PI;

            /* If the distance between two nodes is too great, we put in an intermediate node inbetween */
            while ( angDif > m_maxAngDistance )
            {
                recursionGuard();

                double angle = NormalizeAngle(prevNode.angle - angDif / Math.Ceiling(angDif / m_maxAngDistance));
                Vector3 vector = ellipse.VectorAtAbsoluteAngle(angle);

                ushort newNodeId = NetAccess.CreateNode(centerNodeNetInfo, vector);
                VectorNodeStruct newNode = new VectorNodeStruct(newNodeId);
                newNode.angle = angle;

                ConnectNodes(newNode, prevNode);

                prevNode = newNode;
                angDif = NormalizeAngle(prevNode.angle - p2.angle);
            }

            return prevNode;
        }

        /* Ellipse only. Adds nodes where the ellipse intersects its axes to keep it in shape. User can turn this off. Kepp in mind that since we can only
         * approximate the ellipse (maybe I am wrong), every node on its circumference changes its actual shape. */
        private void InsertIntermediateNodes()
        {
            List<VectorNodeStruct> newNodes = new List<VectorNodeStruct>();
            /* Originally I planned to pair every node on the ellipse to make it symmetric... Didn't work, I gave up on making it work. */
            /*foreach(VectorNodeStruct intersection in intersections.ToArray())
            {
                double angle = getAbsoluteAngle(intersection.vector);
                VectorNodeStruct newNode = new VectorNodeStruct(ellipse.VectorAtAbsoluteAngle(getConjugateAngle(angle)));
                intersections.Add( newNode );
                EllipseTool.Instance.debugDrawPositions.Add(newNode.vector);
            }*/
            newNodes.Add(new VectorNodeStruct(ellipse.VectorAtAngle(0)));
            newNodes.Add(new VectorNodeStruct(ellipse.VectorAtAngle(Math.PI / 2)));
            newNodes.Add(new VectorNodeStruct(ellipse.VectorAtAngle(Math.PI)));
            newNodes.Add(new VectorNodeStruct(ellipse.VectorAtAngle(3 * Math.PI / 2)));
            /*EllipseTool.Instance.debugDrawPositions.Add(new VectorNodeStruct(ellipse.VectorAtAngle(0)).vector);
            EllipseTool.Instance.debugDrawPositions.Add(new VectorNodeStruct(ellipse.VectorAtAngle(Math.PI / 2)).vector);
            EllipseTool.Instance.debugDrawPositions.Add(new VectorNodeStruct(ellipse.VectorAtAngle(Math.PI)).vector);
            EllipseTool.Instance.debugDrawPositions.Add(new VectorNodeStruct(ellipse.VectorAtAngle(3 * Math.PI / 2)).vector);*/
            /* Disgusting nested FOR cycle. We don't want to cluster the nodes too close to each other. */
            foreach(VectorNodeStruct vectorNode in newNodes.ToArray())
            {
                foreach(VectorNodeStruct intersection in intersections)
                {
                    if(VectorDistance(vectorNode.vector, intersection.vector) < DISTANCE_MIN)
                    {
                        newNodes.Remove(vectorNode);
                        //Debug.Log("Node too close, removing from list");
                    }
                }
            }
            intersections.AddRange(newNodes);
        }

        /* See ellipse class */
        private double getAbsoluteAngle(Vector3 absPosition)
        {
            return Ellipse.VectorsAngle(absPosition - ellipse.Center);
        }


        private void ConnectNodes(VectorNodeStruct vectorNode1, VectorNodeStruct vectorNode2)
        {
            bool invert = leftHandTraffic;

            vectorNode1.Create(centerNodeNetInfo);
            vectorNode2.Create(centerNodeNetInfo);

            /* NetNode node1 = GetNode(vectorNode1.nodeId);
             NetNode node2 = GetNode(vectorNode2.nodeId);*/

            double angle1 = getAbsoluteAngle(vectorNode1.vector);
            double angle2 = getAbsoluteAngle(vectorNode2.vector);

            Vector3 vec1 = ellipse.TangentAtAbsoluteAngle(angle1);
            Vector3 vec2 = ellipse.TangentAtAbsoluteAngle(angle2);

            vec1.Normalize();
            vec2.Normalize();
            vec2 = -vec2;

            /*EllipseTool.Instance.debugDrawVector(10*vec1, vectorNode1.vector);
            EllipseTool.Instance.debugDrawVector(10*vec2, vectorNode2.vector);*/

            //NetInfo netPrefab = PrefabCollection<NetInfo>.FindLoaded("Oneway Road");
            NetInfo netPrefab = UI.UIWindow2.instance.dropDown.Value;
            ushort newSegmentId = NetAccess.CreateSegment(vectorNode1.nodeId, vectorNode2.nodeId, vec1, vec2, netPrefab, invert, leftHandTraffic, true);

            /* Sometime in the future ;) */

            try
            {
                SetupTMPE(newSegmentId);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            //Debug.Log(string.Format("Building segment between nodes {0}, {1}, bezier scale {2}", node1, node2, scale));
        }

        private void SetupTMPE(ushort segment)
        {
            if (m_group == null)
                return;
            /* None of this below works: */
            /*bool resultPrev1 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.IsEnteringBlockedJunctionAllowed(segment,false);
            bool resultPrev2 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.IsEnteringBlockedJunctionAllowed(segment,true);
            bool result1 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.SetEnteringBlockedJunctionAllowed(segment, false, true);
            bool result2 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.SetEnteringBlockedJunctionAllowed(segment, true, true);
            bool resultPost1 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.IsEnteringBlockedJunctionAllowed(segment, false);
            bool resultPost2 = TrafficManager.Manager.Impl.JunctionRestrictionsManager.Instance.IsEnteringBlockedJunctionAllowed(segment, true);*/
            /*Debug.Log($"Setting up tmpe segment {segment}. Result: {resultPrev1}, {resultPrev2}, {result1}, {result2}, {resultPost1}, {resultPost2}");
            ModThreading.Timer(segment);*/
            m_group.Actions.Add(new EnteringBlockedJunctionAllowedAction( segment, true, false) );
            m_group.Actions.Add(new EnteringBlockedJunctionAllowedAction( segment, false, false) );
            m_group.Actions.Add(new NoCrossingsAction(segment, true));
            m_group.Actions.Add(new NoCrossingsAction(segment, false));
            m_group.Actions.Add(new NoParkingAction(segment));
        }

        private void recursionGuard()
        {
            pleasenoinfiniterecursion++;
            if (pleasenoinfiniterecursion > 600)
            {
                throw new Exception("Something went wrong! Mod got stuck in infinite recursion.");
            }
        }

        /* Utility */

        private static float VectorDistance(Vector3 v1, Vector3 v2) // Without Y coordinate
        {
            return (float)(Math.Sqrt(Math.Pow(v1.x - v2.x, 2) + Math.Pow(v1.z - v2.z, 2)));
        }
        private static double NormalizeAngle(double angle)
        {
            angle = angle % (2 * Math.PI);
            return angle < 0 ? angle + 2 * Math.PI : angle;
        }
    }

}
