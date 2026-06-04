# Part 2: Blending Cell Colors

https://catlikecoding.com/unity/tutorials/hex-map/part-2/

---

Blending Cell Colors
- Connect neighbors.
- Interpolate colors across triangles.
- Create blend regions.
- Simplify geometry.

This tutorial is the second part of a series about hexagon maps. The previous installment laid the foundation of our grid and gave us the ability to edit cells. Each cell has its own solid color. The color change between cells is abrupt. This time we'll introduce transition zones, blending between colors of adjacent cells.

## Cell Neighbors

Before we can blend between cells colors, we need to know which cells are adjacent to each other. Each cells has six neighbors, which we can identify with a compass direction. The directions are northeast, east, southeast, southwest, west, and northwest. Let's create an enumeration for that and put it in its own script file.

```csharp
public enum HexDirection {
	NE, E, SE, SW, W, NW
}
```

To store these neighbors, add an array to `HexCell`. While we could make it public, instead we'll make it private and provide access methods using a direction. Also ensure that it serializes so the connections survive recompiles.

```csharp
	[SerializeField]
	HexCell[] neighbors;
```

The neighbor array now shows up in the inspector. As each cell has six neighbors, set the array size to 6 for our _Hex Cell_ prefab.

Now add a public method to retrieve a cell's neighbor in one direction. As a direction is always between 0 and 5, we don't need to check whether the index lies within the bounds of the array.

```csharp
	public HexCell GetNeighbor (HexDirection direction) {
		return neighbors[(int)direction];
	}
```

Add a method to set a neighbor too.

```csharp
	public void SetNeighbor (HexDirection direction, HexCell cell) {
		neighbors[(int)direction] = cell;
	}
```

Neighbor relationships are bidirectional. So when setting a neighbor in one direction, it makes sense to immediately set the neighbor in the opposite direction as well.

```csharp
	public void SetNeighbor (HexDirection direction, HexCell cell) {
		neighbors[(int)direction] = cell;
		cell.neighbors[(int)direction.Opposite()] = this;
	}
```

Of course this assumes that we could ask a direction for its opposite. We can support this, by creating an extension method for `HexDirection`. To get the opposite direction, add 3 to the original direction. This only works for the first three directions though, for the others we have to subtract 3 instead.

```csharp
public enum HexDirection {
	NE, E, SE, SW, W, NW
}

public static class HexDirectionExtensions {
	public static HexDirection Opposite (this HexDirection direction) {
		return (int)direction < 3 ? (direction + 3) : (direction - 3);
	}
}
```

### Connecting Neighbors

We can initialize the neighbor relationship in `HexGrid.CreateCell`. As we go through the cells row by row, left to right, we know which cells have already been created. Those are the cells that we can connect to.

The simplest is the E–W connection. The first cell of each row doesn't have a west neighbor. But all other cells in the row do. And these neighbors have been created before the cell we're currently working with. So we can connect them.

```csharp
	void CreateCell (int x, int z, int i) {
		…
		cell.color = defaultColor;

		if (x > 0) {
			cell.SetNeighbor(HexDirection.W, cells[i - 1]);
		}

		Text label = Instantiate<Text>(cellLabelPrefab);
		…
	}
```

We have two more bidirectional connections to make. As these are between different grid rows, we can only connect with the previous row. This means that we have to skip the first row entirely.

```csharp
		if (x > 0) {
			cell.SetNeighbor(HexDirection.W, cells[i - 1]);
		}
		if (z > 0) {
		}
```

As the rows zigzag, they have to be treated differently. Let's first deal with the even rows. As all cells in such rows have a SE neighbor, we can connect to those.

```csharp
		if (z > 0) {
			if ((z & 1) == 0) {
				cell.SetNeighbor(HexDirection.SE, cells[i - width]);
			}
		}
```

We can connect to the SW neighbors as well. Except for the first cell in each row, as it doesn't have one.

```csharp
		if (z > 0) {
			if ((z & 1) == 0) {
				cell.SetNeighbor(HexDirection.SE, cells[i - width]);
				if (x > 0) {
					cell.SetNeighbor(HexDirection.SW, cells[i - width - 1]);
				}
			}
		}
```

The odds rows follow the same logic, but mirrored. Once that's done, all neighbors in our grid are connected.

```csharp
		if (z > 0) {
			if ((z & 1) == 0) {
				cell.SetNeighbor(HexDirection.SE, cells[i - width]);
				if (x > 0) {
					cell.SetNeighbor(HexDirection.SW, cells[i - width - 1]);
				}
			}
			else {
				cell.SetNeighbor(HexDirection.SW, cells[i - width]);
				if (x < width - 1) {
					cell.SetNeighbor(HexDirection.SE, cells[i - width + 1]);
				}
			}
		}
```

Of course not every cell is connected to exactly six neighbors. The cells that form the border of our grid end up with at least two and at most five neighbors. This is something that we have to be aware of.

## Blending Colors

Color blending will make the triangulation of each cell more complex. So let's isolate the code of triangulating a single part. As we have directions now, let's use those to identify the parts, instead of a numeric index.

```csharp
	void Triangulate (HexCell cell) {
		for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++) {
			Triangulate(d, cell);
		}
	}

	void Triangulate (HexDirection direction, HexCell cell) {
		Vector3 center = cell.transform.localPosition;
		AddTriangle(
			center,
			center + HexMetrics.corners[(int)direction],
			center + HexMetrics.corners[(int)direction + 1]
		);
		AddTriangleColor(cell.color);
	}
```

[Additional content about blend regions, solid factors, etc.]
