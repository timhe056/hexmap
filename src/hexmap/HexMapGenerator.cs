using Godot;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 23-26: 程序化地形生成器。
/// 使用预算驱动的 chunk-based flood fill 算法生成大陆地形，
/// 支持多区域生成、侵蚀平滑、气候模拟、河流生成和生物群系分配。
/// </summary>
public partial class HexMapGenerator : Node
{
	[Export]
	public HexGrid Grid { get; set; }

	[Export]
	public bool UseFixedSeed { get; set; }

	[Export]
	public int Seed { get; set; }

	[Export(PropertyHint.Range, "0,0.5,0.01")]
	public float JitterProbability { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "20,200,1")]
	public int ChunkSizeMin { get; set; } = 30;

	[Export(PropertyHint.Range, "20,200,1")]
	public int ChunkSizeMax { get; set; } = 100;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float HighRiseProbability { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "0,0.4,0.01")]
	public float SinkProbability { get; set; } = 0.2f;

	[Export(PropertyHint.Range, "5,95,1")]
	public int LandPercentage { get; set; } = 50;

	[Export(PropertyHint.Range, "1,5,1")]
	public int WaterLevel { get; set; } = 3;

	[Export(PropertyHint.Range, "-4,0,1")]
	public int ElevationMinimum { get; set; } = -2;

	[Export(PropertyHint.Range, "6,10,1")]
	public int ElevationMaximum { get; set; } = 8;

	/* Part 24: 区域和侵蚀参数 */
	[Export(PropertyHint.Range, "0,10,1")]
	public int MapBorderX { get; set; } = 5;

	[Export(PropertyHint.Range, "0,10,1")]
	public int MapBorderZ { get; set; } = 5;

	[Export(PropertyHint.Range, "0,10,1")]
	public int RegionBorder { get; set; } = 4;

	[Export(PropertyHint.Range, "1,4,1")]
	public int RegionCount { get; set; } = 1;

	[Export(PropertyHint.Range, "0,100,1")]
	public int ErosionPercentage { get; set; } = 50;

	/* Part 25: 气候模拟参数 */
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float StartingMoisture { get; set; } = 0.1f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float EvaporationFactor { get; set; } = 0.5f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float PrecipitationFactor { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float RunoffFactor { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float SeepageFactor { get; set; } = 0.125f;

	[Export]
	public HexDirection WindDirection { get; set; } = HexDirection.NW;

	[Export(PropertyHint.Range, "1,10,0.1")]
	public float WindStrength { get; set; } = 4f;

	/* Part 26: 河流和生物群系参数 */
	[Export(PropertyHint.Range, "0,20,1")]
	public int RiverPercentage { get; set; } = 10;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float ExtraLakeProbability { get; set; } = 0.25f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float LowTemperature { get; set; } = 0f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float HighTemperature { get; set; } = 1f;

	public enum HemisphereMode { Both, North, South }

	[Export]
	public HemisphereMode Hemisphere { get; set; } = HemisphereMode.Both;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float TemperatureJitter { get; set; } = 0.1f;

	private RandomNumberGenerator _rng = new RandomNumberGenerator();
	private HexCellPriorityQueue _searchFrontier;
	private int _searchFrontierPhase;
	private int _cellCount;
	private int _landCells;
	private int _temperatureJitterChannel;

	/* Part 24: 区域定义 */
	private struct MapRegion
	{
		public int xMin, xMax, zMin, zMax;
	}

	private List<MapRegion> _regions;

	/* Part 25: 气候数据 */
	private struct ClimateData
	{
		public float Clouds;
		public float Moisture;
	}

	private List<ClimateData> _climate = new List<ClimateData>();
	private List<ClimateData> _nextClimate = new List<ClimateData>();

	/* Part 26: 河流流向候选 */
	private List<HexDirection> _flowDirections = new List<HexDirection>();

	/* Part 26: 生物群系 */
	private struct Biome
	{
		public int Terrain;
		public int Plant;

		public Biome(int terrain, int plant)
		{
			Terrain = terrain;
			Plant = plant;
		}
	}

	private static readonly float[] _temperatureBands = { 0.1f, 0.3f, 0.6f };
	private static readonly float[] _moistureBands = { 0.12f, 0.28f, 0.85f };

	private static readonly Biome[] _biomes = {
		new Biome(0, 0), new Biome(4, 0), new Biome(4, 0), new Biome(4, 0),
		new Biome(0, 0), new Biome(2, 0), new Biome(2, 1), new Biome(2, 2),
		new Biome(0, 0), new Biome(1, 0), new Biome(1, 1), new Biome(1, 2),
		new Biome(0, 0), new Biome(1, 1), new Biome(1, 2), new Biome(1, 3)
	};

	public void GenerateMap(int x, int z)
	{
		GenerateMap(x, z, false);
	}

	public void GenerateMap(int x, int z, bool wrapping)
	{
		if (!UseFixedSeed)
		{
			Seed = (int)(_rng.Randi() & int.MaxValue);
			Seed ^= (int)Time.GetTicksMsec();
			Seed &= int.MaxValue;
		}
		_rng.Seed = (ulong)(uint)Seed;

		_cellCount = x * z;
		Grid.CreateMap(x, z, wrapping);

		if (_searchFrontier == null)
		{
			_searchFrontier = new HexCellPriorityQueue(Grid);
		}

		for (int i = 0; i < _cellCount; i++)
		{
			Grid.CellData[i].values = Grid.CellData[i].values.WithWaterLevel(WaterLevel);
		}

		CreateRegions();
		CreateLand();
		ErodeLand();
		CreateClimate();
		CreateRivers();
		SetTerrainType();

		Grid.RefreshAllCells();
		Grid.RefreshChunks();
	}

	/* Part 24: 创建区域 */
	private void CreateRegions()
	{
		if (_regions == null)
		{
			_regions = new List<MapRegion>();
		}
		else
		{
			_regions.Clear();
		}

		int borderX = Grid.Wrapping ? RegionBorder : MapBorderX;
		MapRegion region;
		switch (RegionCount)
		{
			default:
				if (Grid.Wrapping)
				{
					borderX = 0;
				}
				region.xMin = borderX;
				region.xMax = Grid.CellCountX - borderX;
				region.zMin = MapBorderZ;
				region.zMax = Grid.CellCountZ - MapBorderZ;
				_regions.Add(region);
				break;
			case 2:
				if (_rng.Randf() < 0.5f)
				{
					region.xMin = borderX;
					region.xMax = Grid.CellCountX / 2 - RegionBorder;
					region.zMin = MapBorderZ;
					region.zMax = Grid.CellCountZ - MapBorderZ;
					_regions.Add(region);
					region.xMin = Grid.CellCountX / 2 + RegionBorder;
					region.xMax = Grid.CellCountX - borderX;
					_regions.Add(region);
				}
				else
				{
					if (Grid.Wrapping)
					{
						borderX = 0;
					}
					region.xMin = borderX;
					region.xMax = Grid.CellCountX - borderX;
					region.zMin = MapBorderZ;
					region.zMax = Grid.CellCountZ / 2 - RegionBorder;
					_regions.Add(region);
					region.zMin = Grid.CellCountZ / 2 + RegionBorder;
					region.zMax = Grid.CellCountZ - MapBorderZ;
					_regions.Add(region);
				}
				break;
			case 3:
				region.xMin = borderX;
				region.xMax = Grid.CellCountX / 3 - RegionBorder;
				region.zMin = MapBorderZ;
				region.zMax = Grid.CellCountZ - MapBorderZ;
				_regions.Add(region);
				region.xMin = Grid.CellCountX / 3 + RegionBorder;
				region.xMax = Grid.CellCountX * 2 / 3 - RegionBorder;
				_regions.Add(region);
				region.xMin = Grid.CellCountX * 2 / 3 + RegionBorder;
				region.xMax = Grid.CellCountX - borderX;
				_regions.Add(region);
				break;
			case 4:
				region.xMin = borderX;
				region.xMax = Grid.CellCountX / 2 - RegionBorder;
				region.zMin = MapBorderZ;
				region.zMax = Grid.CellCountZ / 2 - RegionBorder;
				_regions.Add(region);
				region.xMin = Grid.CellCountX / 2 + RegionBorder;
				region.xMax = Grid.CellCountX - borderX;
				_regions.Add(region);
				region.zMin = Grid.CellCountZ / 2 + RegionBorder;
				region.zMax = Grid.CellCountZ - MapBorderZ;
				_regions.Add(region);
				region.xMin = borderX;
				region.xMax = Grid.CellCountX / 2 - RegionBorder;
				_regions.Add(region);
				break;
		}
	}

	private void CreateLand()
	{
		int landBudget = Mathf.RoundToInt(_cellCount * LandPercentage * 0.01f);
		_landCells = landBudget;
		for (int guard = 0; guard < 10000; guard++)
		{
			bool sink = _rng.Randf() < SinkProbability;
			for (int i = 0; i < _regions.Count; i++)
			{
				MapRegion region = _regions[i];
				int chunkSize = _rng.RandiRange(ChunkSizeMin, ChunkSizeMax - 1);
				if (sink)
				{
					landBudget = SinkTerrain(chunkSize, landBudget, region);
				}
				else
				{
					landBudget = RaiseTerrain(chunkSize, landBudget, region);
					if (landBudget == 0)
					{
						return;
					}
				}
			}
		}
		if (landBudget > 0)
		{
			GD.PushWarning($"[HexMapGenerator] Failed to use up {landBudget} land budget.");
			_landCells -= landBudget;
		}
	}

	private int RaiseTerrain(int chunkSize, int budget, MapRegion region)
	{
		_searchFrontierPhase += 1;
		int firstCellIndex = GetRandomCellIndex(region);
		Grid.SearchData[firstCellIndex] = new HexCellSearchData
		{
			searchPhase = _searchFrontierPhase
		};
		_searchFrontier.Enqueue(firstCellIndex);
		HexCoordinates center = Grid.CellData[firstCellIndex].coordinates;

		int rise = _rng.Randf() < HighRiseProbability ? 2 : 1;
		int size = 0;
		while (size < chunkSize && _searchFrontier.TryDequeue(out int index))
		{
			HexCellData current = Grid.CellData[index];
			int originalElevation = current.Elevation;
			int newElevation = originalElevation + rise;
			if (newElevation > ElevationMaximum)
			{
				continue;
			}
			Grid.CellData[index].values = current.values.WithElevation(newElevation);
			if (originalElevation < WaterLevel && newElevation >= WaterLevel && --budget == 0)
			{
				break;
			}
			size += 1;

			for (int i = 0; i < 6; i++)
			{
				HexDirection d = (HexDirection)i;
				if (
					Grid.TryGetCellIndex(current.coordinates.Step(d), out int neighborIndex) &&
					Grid.SearchData[neighborIndex].searchPhase < _searchFrontierPhase
				)
				{
					Grid.SearchData[neighborIndex] = new HexCellSearchData
					{
						searchPhase = _searchFrontierPhase,
						distance = Grid.CellData[neighborIndex].coordinates.DistanceTo(center),
						heuristic = _rng.Randf() < JitterProbability ? 1 : 0
					};
					_searchFrontier.Enqueue(neighborIndex);
				}
			}
		}
		_searchFrontier.Clear();
		return budget;
	}

	private int SinkTerrain(int chunkSize, int budget, MapRegion region)
	{
		_searchFrontierPhase += 1;
		int firstCellIndex = GetRandomCellIndex(region);
		Grid.SearchData[firstCellIndex] = new HexCellSearchData
		{
			searchPhase = _searchFrontierPhase
		};
		_searchFrontier.Enqueue(firstCellIndex);
		HexCoordinates center = Grid.CellData[firstCellIndex].coordinates;

		int sink = _rng.Randf() < HighRiseProbability ? 2 : 1;
		int size = 0;
		while (size < chunkSize && _searchFrontier.TryDequeue(out int index))
		{
			HexCellData current = Grid.CellData[index];
			int originalElevation = current.Elevation;
			int newElevation = current.Elevation - sink;
			if (newElevation < ElevationMinimum)
			{
				continue;
			}
			Grid.CellData[index].values = current.values.WithElevation(newElevation);
			if (originalElevation >= WaterLevel && newElevation < WaterLevel)
			{
				budget += 1;
			}
			size += 1;

			for (int i = 0; i < 6; i++)
			{
				HexDirection d = (HexDirection)i;
				if (
					Grid.TryGetCellIndex(current.coordinates.Step(d), out int neighborIndex) &&
					Grid.SearchData[neighborIndex].searchPhase < _searchFrontierPhase
				)
				{
					Grid.SearchData[neighborIndex] = new HexCellSearchData
					{
						searchPhase = _searchFrontierPhase,
						distance = Grid.CellData[neighborIndex].coordinates.DistanceTo(center),
						heuristic = _rng.Randf() < JitterProbability ? 1 : 0
					};
					_searchFrontier.Enqueue(neighborIndex);
				}
			}
		}
		_searchFrontier.Clear();
		return budget;
	}

	/* Part 24: 侵蚀平滑 */
	private void ErodeLand()
	{
		var erodibleIndices = new List<int>();
		for (int i = 0; i < _cellCount; i++)
		{
			if (IsErodible(i, Grid.CellData[i].Elevation))
			{
				erodibleIndices.Add(i);
			}
		}

		int targetErodibleCount =
			(int)(erodibleIndices.Count * (100 - ErosionPercentage) * 0.01f);

		while (erodibleIndices.Count > targetErodibleCount)
		{
			int index = _rng.RandiRange(0, erodibleIndices.Count - 1);
			int cellIndex = erodibleIndices[index];
			HexCellData cell = Grid.CellData[cellIndex];
			int targetCellIndex = GetErosionTarget(cellIndex, cell.Elevation);

			cell.values = cell.values.WithElevation(cell.Elevation - 1);
			Grid.CellData[cellIndex].values = cell.values;

			HexCellData targetCell = Grid.CellData[targetCellIndex];
			targetCell.values = targetCell.values.WithElevation(targetCell.Elevation + 1);
			Grid.CellData[targetCellIndex].values = targetCell.values;

			if (!IsErodible(cellIndex, cell.Elevation))
			{
				erodibleIndices[index] = erodibleIndices[erodibleIndices.Count - 1];
				erodibleIndices.RemoveAt(erodibleIndices.Count - 1);
			}

			for (int i = 0; i < 6; i++)
			{
				HexDirection d = (HexDirection)i;
				if (
					Grid.TryGetCellIndex(cell.coordinates.Step(d), out int neighborIndex) &&
					Grid.CellData[neighborIndex].Elevation == cell.Elevation + 2 &&
					!erodibleIndices.Contains(neighborIndex)
				)
				{
					erodibleIndices.Add(neighborIndex);
				}
			}

			if (IsErodible(targetCellIndex, targetCell.Elevation) && !erodibleIndices.Contains(targetCellIndex))
			{
				erodibleIndices.Add(targetCellIndex);
			}

			for (int i = 0; i < 6; i++)
			{
				HexDirection d = (HexDirection)i;
				if (
					Grid.TryGetCellIndex(targetCell.coordinates.Step(d), out int neighborIndex) &&
					neighborIndex != cellIndex &&
					Grid.CellData[neighborIndex].Elevation == targetCell.Elevation + 1 &&
					!IsErodible(neighborIndex, Grid.CellData[neighborIndex].Elevation)
				)
				{
					erodibleIndices.Remove(neighborIndex);
				}
			}
		}
	}

	private bool IsErodible(int cellIndex, int cellElevation)
	{
		int erodibleElevation = cellElevation - 2;
		HexCoordinates coordinates = Grid.CellData[cellIndex].coordinates;
		for (int i = 0; i < 6; i++)
		{
			HexDirection d = (HexDirection)i;
			if (
				Grid.TryGetCellIndex(coordinates.Step(d), out int neighborIndex) &&
				Grid.CellData[neighborIndex].Elevation <= erodibleElevation
			)
			{
				return true;
			}
		}
		return false;
	}

	private int GetErosionTarget(int cellIndex, int cellElevation)
	{
		var candidates = new List<int>();
		int erodibleElevation = cellElevation - 2;
		HexCoordinates coordinates = Grid.CellData[cellIndex].coordinates;
		for (int i = 0; i < 6; i++)
		{
			HexDirection d = (HexDirection)i;
			if (
				Grid.TryGetCellIndex(coordinates.Step(d), out int neighborIndex) &&
				Grid.CellData[neighborIndex].Elevation <= erodibleElevation
			)
			{
				candidates.Add(neighborIndex);
			}
		}
		return candidates[_rng.RandiRange(0, candidates.Count - 1)];
	}

	/* Part 25: 气候模拟 */
	private void CreateClimate()
	{
		_climate.Clear();
		_nextClimate.Clear();
		ClimateData initialData = new ClimateData { Moisture = StartingMoisture };
		ClimateData clearData = new ClimateData();
		for (int i = 0; i < _cellCount; i++)
		{
			_climate.Add(initialData);
			_nextClimate.Add(clearData);
		}

		for (int cycle = 0; cycle < 40; cycle++)
		{
			for (int i = 0; i < _cellCount; i++)
			{
				EvolveClimate(i);
			}
			List<ClimateData> swap = _climate;
			_climate = _nextClimate;
			_nextClimate = swap;
		}
	}

	private void EvolveClimate(int cellIndex)
	{
		HexCellData cell = Grid.CellData[cellIndex];
		ClimateData cellClimate = _climate[cellIndex];

		if (cell.IsUnderwater)
		{
			cellClimate.Moisture = 1f;
			cellClimate.Clouds += EvaporationFactor;
		}
		else
		{
			float evaporation = cellClimate.Moisture * EvaporationFactor;
			cellClimate.Moisture -= evaporation;
			cellClimate.Clouds += evaporation;
		}

		float precipitation = cellClimate.Clouds * PrecipitationFactor;
		cellClimate.Clouds -= precipitation;
		cellClimate.Moisture += precipitation;

		float cloudMaximum = 1f - cell.ViewElevation / (ElevationMaximum + 1f);
		if (cellClimate.Clouds > cloudMaximum)
		{
			cellClimate.Moisture += cellClimate.Clouds - cloudMaximum;
			cellClimate.Clouds = cloudMaximum;
		}

		HexDirection mainDispersalDirection = WindDirection.Opposite();
		float cloudDispersal = cellClimate.Clouds * (1f / (5f + WindStrength));
		float runoff = cellClimate.Moisture * RunoffFactor * (1f / 6f);
		float seepage = cellClimate.Moisture * SeepageFactor * (1f / 6f);
		for (int i = 0; i < 6; i++)
		{
			HexDirection d = (HexDirection)i;
			if (!Grid.TryGetCellIndex(cell.coordinates.Step(d), out int neighborIndex))
			{
				continue;
			}
			ClimateData neighborClimate = _nextClimate[neighborIndex];
			if (d == mainDispersalDirection)
			{
				neighborClimate.Clouds += cloudDispersal * WindStrength;
			}
			else
			{
				neighborClimate.Clouds += cloudDispersal;
			}

			int elevationDelta = Grid.CellData[neighborIndex].ViewElevation - cell.ViewElevation;
			if (elevationDelta < 0)
			{
				cellClimate.Moisture -= runoff;
				neighborClimate.Moisture += runoff;
			}
			else if (elevationDelta == 0)
			{
				cellClimate.Moisture -= seepage;
				neighborClimate.Moisture += seepage;
			}

			_nextClimate[neighborIndex] = neighborClimate;
		}

		ClimateData nextCellClimate = _nextClimate[cellIndex];
		nextCellClimate.Moisture += cellClimate.Moisture;
		if (nextCellClimate.Moisture > 1f)
		{
			nextCellClimate.Moisture = 1f;
		}
		_nextClimate[cellIndex] = nextCellClimate;
		_climate[cellIndex] = new ClimateData();
	}

	/* Part 26: 河流生成 */
	private void CreateRivers()
	{
		var riverOrigins = new List<int>();
		for (int i = 0; i < _cellCount; i++)
		{
			HexCellData cell = Grid.CellData[i];
			if (cell.IsUnderwater)
			{
				continue;
			}
			ClimateData data = _climate[i];
			float weight = data.Moisture * (cell.Elevation - WaterLevel) / (ElevationMaximum - WaterLevel);
			if (weight > 0.75f)
			{
				riverOrigins.Add(i);
				riverOrigins.Add(i);
			}
			if (weight > 0.5f)
			{
				riverOrigins.Add(i);
			}
			if (weight > 0.25f)
			{
				riverOrigins.Add(i);
			}
		}

		int riverBudget = Mathf.RoundToInt(_landCells * RiverPercentage * 0.01f);
		while (riverBudget > 0 && riverOrigins.Count > 0)
		{
			int index = _rng.RandiRange(0, riverOrigins.Count - 1);
			int lastIndex = riverOrigins.Count - 1;
			int originIndex = riverOrigins[index];
			riverOrigins[index] = riverOrigins[lastIndex];
			riverOrigins.RemoveAt(lastIndex);

			if (!Grid.CellData[originIndex].HasRiver)
			{
				bool isValidOrigin = true;
				HexCellData origin = Grid.CellData[originIndex];
				for (int i = 0; i < 6; i++)
				{
					HexDirection d = (HexDirection)i;
					if (
						Grid.TryGetCellIndex(origin.coordinates.Step(d), out int neighborIndex) &&
						(Grid.CellData[neighborIndex].HasRiver || Grid.CellData[neighborIndex].IsUnderwater)
					)
					{
						isValidOrigin = false;
						break;
					}
				}
				if (isValidOrigin)
				{
					riverBudget -= CreateRiver(originIndex);
				}
			}
		}

		if (riverBudget > 0)
		{
			GD.PushWarning($"[HexMapGenerator] Failed to use up {riverBudget} river budget.");
		}
	}

	private int CreateRiver(int originIndex)
	{
		int length = 1;
		int cellIndex = originIndex;
		HexCellData cell = Grid.CellData[cellIndex];
		HexDirection direction = HexDirection.NE;
		while (!cell.IsUnderwater)
		{
			int minNeighborElevation = int.MaxValue;
			_flowDirections.Clear();
			for (int i = 0; i < 6; i++)
			{
				HexDirection d = (HexDirection)i;
				if (!Grid.TryGetCellIndex(cell.coordinates.Step(d), out int neighborIndex))
				{
					continue;
				}
				HexCellData neighbor = Grid.CellData[neighborIndex];

				if (neighbor.Elevation < minNeighborElevation)
				{
					minNeighborElevation = neighbor.Elevation;
				}

				if (neighborIndex == originIndex || neighbor.HasIncomingRiver)
				{
					continue;
				}

				int delta = neighbor.Elevation - cell.Elevation;
				if (delta > 0)
				{
					continue;
				}

				if (neighbor.HasOutgoingRiver)
				{
					Grid.CellData[cellIndex].flags = cell.flags.WithRiverOut(d);
					Grid.CellData[neighborIndex].flags = neighbor.flags.WithRiverIn(d.Opposite());
					return length;
				}

				if (delta < 0)
				{
					_flowDirections.Add(d);
					_flowDirections.Add(d);
					_flowDirections.Add(d);
				}
				if (
					length == 1 ||
					(d != direction.Next2() && d != direction.Previous2())
				)
				{
					_flowDirections.Add(d);
				}
				_flowDirections.Add(d);
			}

			if (_flowDirections.Count == 0)
			{
				if (length == 1)
				{
					return 0;
				}

				if (minNeighborElevation >= cell.Elevation)
				{
					cell.values = cell.values.WithWaterLevel(minNeighborElevation);
					if (minNeighborElevation == cell.Elevation)
					{
						cell.values = cell.values.WithElevation(minNeighborElevation - 1);
					}
					Grid.CellData[cellIndex].values = cell.values;
				}
				break;
			}

			direction = _flowDirections[_rng.RandiRange(0, _flowDirections.Count - 1)];
			cell.flags = cell.flags.WithRiverOut(direction);
			Grid.TryGetCellIndex(cell.coordinates.Step(direction), out int outIndex);
			Grid.CellData[outIndex].flags = Grid.CellData[outIndex].flags.WithRiverIn(direction.Opposite());

			length += 1;

			if (
				minNeighborElevation >= cell.Elevation &&
				_rng.Randf() < ExtraLakeProbability
			)
			{
				cell.values = cell.values.WithWaterLevel(cell.Elevation);
				cell.values = cell.values.WithElevation(cell.Elevation - 1);
			}
			Grid.CellData[cellIndex] = cell;
			cellIndex = outIndex;
			cell = Grid.CellData[cellIndex];
		}
		return length;
	}

	/* Part 26: 生物群系分配 */
	private void SetTerrainType()
	{
		_temperatureJitterChannel = _rng.RandiRange(0, 3);
		int rockDesertElevation = ElevationMaximum - (ElevationMaximum - WaterLevel) / 2;

		for (int i = 0; i < _cellCount; i++)
		{
			HexCellData cell = Grid.CellData[i];
			float temperature = DetermineTemperature(i, cell);
			float moisture = _climate[i].Moisture;
			if (!cell.IsUnderwater)
			{
				int t = 0;
				for (; t < _temperatureBands.Length; t++)
				{
					if (temperature < _temperatureBands[t])
					{
						break;
					}
				}
				int m = 0;
				for (; m < _moistureBands.Length; m++)
				{
					if (moisture < _moistureBands[m])
					{
						break;
					}
				}
				Biome cellBiome = _biomes[t * 4 + m];

				if (cellBiome.Terrain == 0)
				{
					if (cell.Elevation >= rockDesertElevation)
					{
						cellBiome.Terrain = 3;
					}
				}
				else if (cell.Elevation == ElevationMaximum)
				{
					cellBiome.Terrain = 4;
				}

				if (cellBiome.Terrain == 4)
				{
					cellBiome.Plant = 0;
				}
				else if (cellBiome.Plant < 3 && cell.HasRiver)
				{
					cellBiome.Plant += 1;
				}

				Grid.CellData[i].values = cell.values.
					WithTerrainTypeIndex(cellBiome.Terrain).
					WithPlantLevel(cellBiome.Plant);
			}
			else
			{
				int terrain;
				if (cell.Elevation == WaterLevel - 1)
				{
					int cliffs = 0, slopes = 0;
					for (int d = 0; d < 6; d++)
					{
						if (!Grid.TryGetCellIndex(
							cell.coordinates.Step((HexDirection)d), out int neighborIndex))
						{
							continue;
						}
						int delta = Grid.CellData[neighborIndex].Elevation - cell.WaterLevel;
						if (delta == 0)
						{
							slopes += 1;
						}
						else if (delta > 0)
						{
							cliffs += 1;
						}
					}

					if (cliffs + slopes > 3)
					{
						terrain = 1;
					}
					else if (cliffs > 0)
					{
						terrain = 3;
					}
					else if (slopes > 0)
					{
						terrain = 0;
					}
					else
					{
						terrain = 1;
					}
				}
				else if (cell.Elevation >= WaterLevel)
				{
					terrain = 1;
				}
				else if (cell.Elevation < 0)
				{
					terrain = 3;
				}
				else
				{
					terrain = 2;
				}

				if (terrain == 1 && temperature < _temperatureBands[0])
				{
					terrain = 2;
				}
				Grid.CellData[i].values = cell.values.WithTerrainTypeIndex(terrain);
			}
		}
	}

	private float DetermineTemperature(int cellIndex, HexCellData cell)
	{
		float latitude = (float)cell.coordinates.Z / Grid.CellCountZ;
		if (Hemisphere == HemisphereMode.Both)
		{
			latitude *= 2f;
			if (latitude > 1f)
			{
				latitude = 2f - latitude;
			}
		}
		else if (Hemisphere == HemisphereMode.North)
		{
			latitude = 1f - latitude;
		}

		float temperature = Mathf.Lerp(LowTemperature, HighTemperature, latitude);

		temperature *= 1f - (cell.ViewElevation - WaterLevel) /
			(ElevationMaximum - WaterLevel + 1f);

		float jitter = HexMetrics.SampleNoise(Grid.CellPositions[cellIndex] * 0.1f)[_temperatureJitterChannel];

		temperature += (jitter * 2f - 1f) * TemperatureJitter;

		return temperature;
	}

	private int GetRandomCellIndex(MapRegion region)
	{
		return Grid.GetCellIndex(
			_rng.RandiRange(region.xMin, region.xMax - 1),
			_rng.RandiRange(region.zMin, region.zMax - 1)
		);
	}
}
