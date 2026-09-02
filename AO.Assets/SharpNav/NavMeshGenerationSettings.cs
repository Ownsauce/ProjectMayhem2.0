// Copyright (c) 2014-2015 Robert Rouhani <robert.rouhani@gmail.com> and other contributors (see CONTRIBUTORS file).
// Licensed under the MIT License - https://raw.github.com/Robmaister/SharpNav/master/LICENSE

using System;

namespace SharpNav
{
	/// <summary>
	/// Contains all the settings necessary to convert a mesh to a navmesh.
	/// </summary>
	public class NavMeshGenerationSettings
	{
        public static NavMeshGenerationSettings CustomDensity(float cellSize, float padding = 1f)
        {
            var settings = new NavMeshGenerationSettings
            {
                CellSize = cellSize,
                CellHeight = cellSize * 0.1f,
                MaxClimb = cellSize * (1 + (float)Math.Sqrt(3)),
                AgentHeight = 1.7f,
                AgentRadius = padding,
                MinRegionSize = 8,
                MergedRegionSize = 20,
                MaxEdgeLength = 12,
                MaxEdgeError = 1.8f,
                VertsPerPoly = 6,
                SampleDistance = 6,
                MaxSampleError = 1,
                BuildBoundingVolumeTree = true,
                FilterLargestSection = false
            };

            return settings;
        }

        public static NavMeshGenerationSettings LowDensity
        {
            get
            {
                var settings = new NavMeshGenerationSettings
                {
                    CellSize = 1f,
                    CellHeight = 1f,
                    MaxClimb = 2.73f,
                    AgentHeight = 1.7f,
                    AgentRadius = 1f,
                    MinRegionSize = 8,
                    MergedRegionSize = 20,
                    MaxEdgeLength = 12,
                    MaxEdgeError = 1.8f,
                    VertsPerPoly = 6,
                    SampleDistance = 6,
                    MaxSampleError = 1,
                    BuildBoundingVolumeTree = true,
                    FilterLargestSection = false
                };

                return settings;
            }
        }

        public static NavMeshGenerationSettings HighDensity
        {
            get
            {
                var settings = new NavMeshGenerationSettings
                {
                    CellSize = 0.08f,
                    CellHeight = 0.15f,
                    MaxClimb = 0.3f,
                    AgentHeight = 1.7f,
                    AgentRadius = 0.8f,
                    MinRegionSize = 8,
                    MergedRegionSize = 20,
                    MaxEdgeLength = 1,
                    MaxEdgeError = 1.8f,
                    VertsPerPoly = 6,
                    SampleDistance = 6,
                    MaxSampleError = 1,
                    BuildBoundingVolumeTree = true,
                    FilterLargestSection = false
                };

                return settings;
            }
        }

        public static NavMeshGenerationSettings MediumDensity
        {
            get
            {
                var settings = new NavMeshGenerationSettings
                {
                    CellSize = 0.3f,
                    CellHeight = 0.3f,
                    MaxClimb = 0.81f,
                    AgentHeight = 1.7f,
                    AgentRadius = 2f,
                    MinRegionSize = 8,
                    MergedRegionSize = 20,
                    MaxEdgeLength = 12,
                    MaxEdgeError = 1.8f,
                    VertsPerPoly = 6,
                    SampleDistance = 6,
                    MaxSampleError = 1,
                    BuildBoundingVolumeTree = true,
                    FilterLargestSection = false
                };

                return settings;
            }
        }
        /// <summary>
        /// Gets or sets the size of a cell in the X and Z axes in world units.
        /// </summary>
        /// 
        public float CellSize { get; set; }

		/// <summary>
		/// Gets or sets the height of a cell in world units.
		/// </summary>
		public float CellHeight { get; set; }

		/// <summary>
		/// Gets or sets the maximum climb height.
		/// </summary>
		public float MaxClimb { get; set; }

		/// <summary>
		/// Gets or sets the height of the agents traversing the <see cref="NavMesh"/>.
		/// </summary>
		public float AgentHeight { get; set; }

		/// <summary>
		/// Gets or sets the radius of the agents traversing the <see cref="NavMesh"/>.
		/// </summary>
		public float AgentRadius { get; set; }

		/// <summary>
		/// Gets or sets the minimum number of spans that can form a region. Any less than this, and they will be
		/// merged with another region.
		/// </summary>
		public int MinRegionSize { get; set; }

		/// <summary>
		/// Gets or sets the size of the merged regions
		/// </summary>
		public int MergedRegionSize { get; set; }

		/// <summary>
		/// Gets or sets the maximum edge length allowed
		/// </summary>
		public float MaxEdgeLength { get; set; }

		/// <summary>
		/// Gets or sets the maximum error allowed
		/// </summary>
		public float MaxEdgeError { get; set; }

		/// <summary>
		/// Gets or sets the flags that determine how the <see cref="ContourSet"/> is generated.
		/// </summary>
		public ContourBuildFlags ContourFlags { get; set; }

		/// <summary>
		/// Gets or sets the number of vertices a polygon can have.
		/// </summary>
		public int VertsPerPoly { get; set; }

		/// <summary>
		/// Gets or sets the sampling distance for the PolyMeshDetail
		/// </summary>
		public float SampleDistance { get; set; }

		/// <summary>
		/// Gets or sets the maximium error allowed in sampling for the PolyMeshDetail
		/// </summary>
		public float MaxSampleError { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether a bounding volume tree is generated for the mesh.
		/// </summary>
		public bool BuildBoundingVolumeTree { get; set; }

        /// <summary>
        /// Exports the largest section of a baked navmesh
        /// </summary>
        public bool FilterLargestSection { get; set; }

        /// <summary>
        ///// Gets or sets the off mesh links for the mesh.
        ///// </summary>
        ///// 
        //public List<OffMeshConnection> Links { get; set; }

        /// <summary>
        /// Gets the height of the agents traversing the <see cref="NavMesh"/> in voxel (cell) units.
        /// </summary>
		public int VoxelAgentHeight
		{
			get
			{
				return (int)(AgentHeight / CellHeight);
			}
		}

        /// <summary>
        /// Gets the maximum clim height in voxel (cell) units.
        /// </summary>
        public int VoxelMaxClimb
        {
            get
			{
				return (int)(MaxClimb / CellHeight);
			}
		}

        /// <summary>
        /// Gets the radius of the agents traversing the <see cref="NavMesh"/> in voxel (cell) units.
        /// </summary>
        public int VoxelAgentRadius
		{
			get
			{
				return (int)(AgentRadius / CellHeight);
			}
		}
	}
}
