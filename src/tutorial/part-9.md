Hex Map 9

Terrain Features

Add detail objects to the terrain.

Support feature density levels.

Use a variety of objects per level.

Mix three different feature types.

This tutorial is the ninth part of a series about hexagon maps. This installment is about adding details to the terrain. Features like buildings and trees.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/tutorial-image.jpg)

Adding Support for Features

While the shape of our terrain has variation, there isn't much going on. It is a lifeless place. To make it come alive, we need to add things like trees and building. These features are not part of the terrain mesh. They are separate objects. But that doesn't stop us from adding them when triangulating the terrain.

HexGridChunk doesn't care about how a mesh works. It simply orders one of its HexMesh children to add a triangle, or a quad. Likewise, it can have a child that takes care of feature placement for it.

Feature Manager

Let's create a HexFeatureManager component that's responsible for the features of a single chunk. Using the same design as HexMesh, we'll give it a Clear, an Apply, and an AddFeature method. As features have to be placed somewhere, the AddFeature method gets a position parameter.

We begin with a stub implementation that doesn't actually do anything.



We can now add a reference to such a component to HexGridChunk. Then we can include it in the triangulation process, just like all the HexMesh children.



Let's start by placing a single feature in the center of every cell.



Now we need the actual feature manager. Add another child object to the Hex Grid Chunk prefab and give it a HexFeatureManager component. Then we can connect the chunk to it.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/hierarchy.png)

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/features-child-object.png)

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/chunk-prefab.png)

Feature Prefab

What kind of feature shall we make? For our first test, a cube will do. Create a fairly large cube, say scaled to (3, 3, 3), and turn it into a prefab. Create a material for it as well. I used a default material with a red color. Remove its collider, as we don't need it.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/feature-prefab.png)

Our feature managers need a reference to this prefab, so add one to HexFeatureManager, then hook them up. Because placement requires accessing the transform component, use that as the reference type.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/manager-with-prefab.png)



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/instances.png)

From now on, the terrain will be filled with cubes. At least, the top half of cubes. Because the local origin of Unity's cube mesh lies at the center of the cube, the bottom half is submerged below the terrain surface. To place the cubes on top of the terrain, we have to move them upwards by half their height.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/instances-moved.png)

What if we're using a different mesh?

This approach is specifically for the default cube. If you're using a custom mesh, it is a better idea to design them so their local origin sits at their bottom. Then you don't have to adjust the position at all.

Of course our cells are perturbed, so we should perturb the position of our features as well. That does away with the perfect regularity of the grid.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/adding-support-for-features/instances-perturbed.png)



Isn't it inefficient to create and destroy features all the time?

It sure feels like it. But we shouldn't be concerned about that at this time. First we get feature placement right. Once we've covered that, and it turns out to be a bottleneck, then we can get smart about efficiency. That's when we might end up using the HexFeatureManager.Apply method as well. But that's for a future tutorial. Fortunately, it really isn't that bad, because we've split the terrain into chunks.

unitypackage

Feature Placement

We're currently placing a feature in the center of every cell. This is fine for otherwise empty cells. But it doesn't look good for cells that contain rivers and roads, or that are underwater.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-placement/everywhere.png)

So let's make sure that a cell is clear before we add a feature to it in HexGridChunk.Triangulate.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-placement/limited.png)

One Feature Per Direction

Having only a single feature per cell isn't very much. There's plenty of room for more. Let's add an addition feature to the center of each of a cell's six triangles. So one per direction.

We do this in the other Triangulate method, when we know that there isn't a river. We still have to check whether we're underwater or whether there's a road. But in this case, we only care about roads going in the current direction.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-placement/many-features.png)

This produces a lot more features! They appear next to roads, but they still shy away from rivers. To get features along rivers, we can also add them when inside TriangulateAdjacentToRiver. But once again only when not underwater, and not on top of a road.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-placement/features-adjacent-to-river.png)

Can we render that many objects?

Many features would produce many draw calls, but Unity's dynamic batching helps us out here. As the features are small, their meshes should have few vertices. That allows many of them to be combined in a single batch. But if it turns out to be a bottleneck, we'll deal with it later. It is also possible to use instancing, which is comparable to dynamic batching when using many small meshes.

unitypackage

Feature Variety

All our feature objects have the exact same orientation, which doesn't look organic at all. So let's give each a random rotation.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-variety/rotated.png)

This produces a much more varied result. Unfortunately, every time a chunk is refreshed, its features end up with new random rotations. Editing something shouldn't case the nearby features to spasm, so we need a different approach.

We have a noise texture, which is always the same. However, that texture contains Perlin gradient noise, which is locally coherent. This is exactly what we want when perturbing the cell positions of vertices. But we don't need coherent rotations. All rotations should be equally likely and mixed up. So what we need is a texture with non-gradient random values, and sample it without bilinear filtering. That is actually a hash grid, which forms the basis for gradient noise.

Creating a Hash Grid

We can create a hash grid with an array of floats and fill it once with random values. That way we don't need a texture at all. Let's add it to HexMetrics. 256 by 256 should offer enough variety.



The random values are generated by a mathematical formula that always produces the same results. Which sequence you get depends on a seed number, which defaults to the current time value. That's why you get different results each play session.

To allow recreation of the exact same features, we have to add a seed parameter to our initialization method.



Now that we have initialized the random number stream, we'll always get the same sequence out of it. So all supposedly random events that would happen after generating the map will always be the same as well. We can prevent this by saving the state of the random number generator before initializing it. After we're done, we set it back to its old state.



Initialization of the hash grid is done by HexGrid, at the same time that it assigns the noise texture. So that's in HexGrid.Start and HexGrid.Awake. Make sure that we're not generating it more often than necessary.



The public seed allows us to choose a seed value for the map. Any value will do. I picked 1234.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-variety/seed.png)

Using the Hash Grid

To make use of the hash grid, add a sampling method to HexMetrics. Like SampleNoise, it uses the XZ coordinates of a position to retrieve a value. The hash index is found by clamping the coordinates to integer values, then taking the remainder of the integer division by the grid size.



What does % do?

This is the modulo operator. It computer the remainder of divisions, in our case integer divisions. For example, the sequence &minus;4, &minus;3, &minus;2, &minus;1, 0, 1, 2, 3, 4 modulo 3 becomes &minus;1, 0, &minus;2, &minus;1, 0, 1, 2, 0, 1.

This works for positive coordinates, but not for negative coordinates, as the remainder would be negative for those numbers. We can fix that by adding the grid size to negative results.



Now we produce a different value for each square unit. We don't actually need a grid this dense. The features are further apart than that. We can stretch the grid by scaling down the position before computing the index. A unique value per 4 by 4 square should be sufficient.



Go back to HexFeatureManager.AddFeature and use our new hash grid to obtain a value. Once we use that to set the rotation, our features will remain motionless when we edit the terrain.



Placement Threshold

While features have varying rotations, their placement still has an obvious pattern. Every cell has seven features crowding it. We can introduce chaos to this setup by arbitrarily omitting some of the features. How can we decide whether to add a feature or not? By consulting another random value!

So now we need two hash values instead of one. We support this by using Vector2 instead of float as our hash grid array type. But vector operations don't make sense for our hash values, so let's create a special struct for this purpose. All it needs are two floats. And let's add a static method to create a randomized value pair.



Doesn't it need to be serializable?

We're only storing these structures in our hash grid, which is static so isn't serialized by Unity during recompiles. So it doesn't need to be serializable.

Adjust HexMetrics so it uses this new struct.



Now HexFeatureManager.AddFeature has access to two hash values. Let's use the first one to decide whether we actually add a feature, or skip it. If the value is 0.5 or larger, we bail. This will eliminate about half of the features. We use the second value to determine the rotation, as usual.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/feature-variety/threshold.png)

unitypackage

Painting Features

Instead of placing features everywhere, let's make them editable. But we're not going to paint individual features. Instead, we'll add a feature level to every cell. This level controls the likelihood of features appearing in the cell. The default is zero, which guarantees that there are no features present.

As our red cubes don't look like natural features of the terrain, let's say that they are buildings. They represent urban development. So let's add an urban level to HexCell.



We could ensure that the urban level is zero for underwater cell, but that is not necessary. We already omit features when underwater. And maybe we'll add urban water features at some point, like docks or underwater structures.

Density Slider

To edit the urban level, add support for another slider to HexMapEditor.



Add another slide to the UI and connect it to the appropriate methods. I put it in a new panel on the right side of the screen, to prevent overcrowding of the left panel.

How many levels do we need? Let's stick to four, representing zero, low, medium, and high density development.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/painting-features/slider.png)

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/painting-features/slider-inspector.png)

Adjusting the Threshold

Now that we have an urban level, we have to use that to determine whether we place features or not. To do so, we have to add the urban level as an extra parameter to HexFeatureManager.AddFeature. Let's go one step further and just pass along the cell itself. That will be more convenient later.

A quick way to make use of the urban level is to multiply it by 0.25 and use that as the new threshold to bail. That way, the probability of a feature appearing increases by 25% per level.



To make this work, pass along the cells in HexGridChunk.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/painting-features/urban-levels.png)

unitypackage

Multiple Feature Prefabs

A difference in feature probability is not sufficient to create a clear distinction between lower and higher urban levels. Some cells simply end up with fewer or more buildings than expected. We can make the difference much clearer by using a different prefab for each level.

Get rid of the featurePrefab field in HexFeatureManager and replace it with an array for the urban prefabs. Use the urban level minus one as an index to retrieve the appropriate prefab.



Create two duplicates of the feature prefab and rename and adjust them to represent the three different urban levels. Level 1 is low density, so I used a unit-sized cube to represent a hovel. I set the scale of the level 2 prefab to (1.5, 2, 1.5) to suggest a larger two-story building. For level 3, I used (2, 5, 2) to indicate a high-rise.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/multiple-prefabs-inspector.png)

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/multiple-prefabs.png)

Mixing Prefabs

We don't need to limit ourselves to a strict segregation of building type. We can mix them a bit, just like in the real world. Instead of using a single threshold per level, let's use three per level, one per building type.

For level 1, let's use a 40% chance for a hovel. The other building won't appear at all. This requires the threshold triplet (0.4, 0, 0).

For level 2, let's replace the hovels with larger buildings, and add a 20% chance for additional hovels. Still no high-rises. That suggests the threshold triplet (0.2, 0.4, 0).

For level 3, let's upgrade the medium buildings to high-rises, replace the hovels again, and add another 20% change for more hovels. The thresholds for that would be (0.2, 0.2, 0.4).

So the idea is that we upgrade existing building and add new ones in empty lots as the urban level increases. To replace an existing building, we have to use the same hash value ranges. If hashes between 0 and 0.4 were hovels at level 1, the same range should produce high-rises at level 3. Specifically, at level 3 high-rises should spawn for hash values in the 0&ndash;0.4 range, the two-story houses in the 0.4&ndash;0.6 range, and the hovels in the 0.6&ndash;0.8 range. If we check them from highest to lowest, we can do this with the threshold triplet (0.4, 0.6, 0.8). The level 2 thresholds then become (0, 0.4, 0.6), and the level 1 thresholds become (0, 0, 0.4).

Let's store these thresholds in HexMetrics as a collection of arrays, with a method to get the thresholds for a specific level. As we're only concerned with levels that have features, we ignore level 0.



Next, we add a method to HexFeatureManager which uses a level and hash value to select a prefab. If the level is larger than zero, we retrieve the thresholds using the level decreased by one. Then we loop through the thresholds until one exceeds the hash value. That means we found a prefab. If we didn't, we return null.



This approach requires us to reorder the prefab references so they go from high to low density.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/reversed-prefabs.png)

Use this new method in AddFeature to pick a prefab. If we end up without one, bail. Otherwise, instantiate it and continue as before.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/mixing-prefabs.png)

Variation Per Level

By now we have a nice mix of buildings, but it are still just three distinct ones. We can increase the variety even more by associating a collection of prefabs to each urban density level. Then we pick one of those at random. This requires a new random value, so add a third one to HexHash.



Turn HexFeatureManager.urbanPrefabs into an array of arrays, and add a choice parameter to the PickPrefab method. Use it to index the nested array by multiplying it with that array's length and casting to an integer.



Let's base this choice on the second hash value, B. This requires that the rotation changes from B to C.



Before we continue, we have to be aware that Random.value can produce the value 1. This would cause our array index to go out of bounds. To guarantee that this doesn't happen, scale the hash values down a little bit. Just scale them all, so we don't need to worry about which one we use.



Unfortunately, the inspector does not show arrays of arrays. So we cannot configure them. To work around this, create a serializable struct that encapsulates the nested array. Give it a method that takes care of the conversion from a choice to an array index and returns the prefab.



Use an array of these collections in HexFeatureManager, instead of the nested arrays.



You can now define multiple buildings per density level. As they're independent, you don't need to use the same amount per level. I simply used two variants per level, adding a longer lower variant to each. I set their scales to (3.5, 3, 2), (2.75, 1.5, 1.5), and (1.75, 1, 1).

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/collections-inspector.png)

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-prefabs/collections.png)

unitypackage

Multiple Feature Types

With our current setup we can create decent urban settings. But terrain can contain more than just buildings. What about farms? What about plants? Let's add levels for those to HexCell as well. They're not exclusive, they can mix.



Of course this requires support for two addition slides in HexMapEditor.



Add them to the UI, as expected.

![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-types/sliders.png)

And HexFeatureManager needs additional collections as well.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-types/three-sets-of-collections.png)

I gave both the farms and plants two prefabs per density level, just like the urban collections. I used cubes for all of them. The farms got a light green material, while the plants got a dark green material.

I made the farm cubes 0.1 units high, to represent rectangular plots of farmland. The high-density scales are (2.5, 0.1, 2.5) and (3.5, 0.1, 2). The medium lots are 1.75 square and 2.5 by 1.25. Low density got 1 square and 1.5 by 0.75.

The plant prefabs represent tall trees and large shrubs. The high-density ones are biggest, at (1.25, 4.5, 1.25) and (1.5, 3, 1.5). Medium scales are (0.75, 3, 0.75) and (1, 1.5, 1). The smallest plants have sizes (0.5, 1.5, 0.5) and (0.75, 1, 0.75).

Feature Selection

Each feature type should get its own hash value, so they have different spawn patterns. This makes it possible to mix them. So add two additional values to HexHash.



HexFeatureManager.PickPrefab now has to work with different collections. Add a parameter to it to facilitate this. Also, change the hash used for the prefab variant choice to D, and the one for the rotation to E.



Currently AddFeature picks an urban prefab. That's fine, but now we have more options. So let's pick another prefab as well, from the farms. We'll use B as its hash value. The variant choice can just rely on D again.



Which prefab do we end up instantiating? If one of them ends up as null, then the choice is clear. But when both exist, we have to make a decision. Let's just use the one with the lowest hash value.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-types/urban-farm.png)

Next, we do the same for plants, using the C hash value.



However, we can't just copy the code like that. When we end up picking farm instead of urban, we should compare the plant hash with the farm hash. Not with the urban hash. So we have to keep track of which hash we decided to go with, and compare with that one.



![](https://catlikecoding.com/unity/tutorials/hex-map/part-9/multiple-feature-types/urban-farm-plant.png)

The next tutorial is Walls.

unitypackage
