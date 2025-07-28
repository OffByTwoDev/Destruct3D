# Multi Scale Procedural Destruction in Godot

A Godot addon written in C# that generates fragments on startup, and removes these fragments in a customisable way, in realtime.

No knowledge of C# is needed to use the plugin or customise the basic behaviour.

# Instructions

### Instructions to create a destructible object

- cntrl+shift+a -> add destronoiNode
- add a meshInstance & collisionshape like you would for any rigidbody
- set the binary tree depth (recommend 8 for most cases)
- set the fragmentContainer to be the node where you want fragments to be instantiated under (e.g. an "Environment" or "Objects" node is appropriate)


optional:
- if you want to use textures, set hasTexturedMaterial to true, and add a fragmentMaterial (to be applied to the fresh interior faces) and add a material to your meshInstance (to be used for the exterior faces)
- if you want the largest fragments to be kept stationary (e.g. if you want to remove rigidbody fragments from an object which acts like a staticbody3D) then set treatTopMostLevelAsStatic to true

requirements:
- if you need your level to be reloadable, then set your meshInstance's Mesh -> LocalToScene = true (textures will appear black after reloading the scene if not)

### Instructions to destroy a destructible object

To actually destroy an object, you need to call Activate() on a VSTSplittingComponent (which inherits from Area3D) that overlaps with a destronoiNode. The Example scene has an example of this, where Activate() is called upon pressing a keybind. In a videogame you'll more than likely want to call Activate() when your object collides with something (e.g. for an RPG), or after a timer is up (e.g. for a grenade)

# Credits

I am extremely thankful to seadaemon for the [Destronoi plugin](https://github.com/seadaemon/Destronoi), which this plugin was built off of. If Destronoi didn't exist this project would've taken significantly longer and I may never have made it.

For clarity: my C# code "VSTNode.cs" and "DestronoiNode.cs" were initially a direct port of seadaemon's gdscript code. I then added some functions and different components to each to create this project.

# Dependencies

- Godot 4.4.1+ - .NET version

Note: Jolt is highly recommended when using this plugin. Using the default engine leads to fragments oscillating unphysically (might be something to do with Centre of Mass calculations).

# Technicalities / Theory

On startup, each destronoi node creates a binary tree of a specified depth (i.e. the bottom-most layer has 2^depth total fragments). The top level is the mesh, the 2nd level is 2 halves of the mesh, the 3rd is 4 quarters of the mesh, the 4th is 8 eighths of the mesh, etc.

VSTSplittingComponent.Unsplit() searches this tree, and instantiates fragments of a specified depth which are within a bounding area3d as new destronoi Nodes. VSTSplittingComponent.Activate() does this twice per explosion on 2 different explosion depths and spatial scales, so that the fragments closer to the explosion that are freed are smaller than ones further away (to simulate some level of realism & multiscale behaviour).

If the fragmentation splits the parent fragment into more than 1 segment (e.g. if an explosion happens in the middle of a long beam), VSTUnsplittingComponent detects this and creates however many new parent fragments are needed.

This way, removing any subset of the fragments is supported and will be accurately represented by the instantiated meshes. This plugin does not just convert a mesh into its fragments, it removes subsets of fragments and recalculates what the remaining parent fragment(s) would look like.

Once fragments are removed, the binary tree(s) are updated, so that any fragments that have been removed have their references nullified. This way the "sum" of the bottom-most meshinstances (that are gettable-to) in any VST always accurately represents the current state of the destronoi node.

There is also some (beta) code for unfragmentation i.e. forming a parent fragment from a list of children (although this is probably a lot less useful than the fragmentation code).

# Recommendations & Performance

For comparison, my system is:
- AMD Ryzen 5 5600 (3.5 GHz)
- AMD Radeon 6600XT (8GB VRAM)
- 32GB RAM

I would recommend using a binary tree depth of 8 (so $2^8 = 256$ fragments) as a starting point. This seems to give good looking destruction (if your object is like 5m ish) and has reasonable startup time for ~10 objects (~5 seconds for me).

I would recommend setting treeHeight to e.g. 3 whilst you are making parts of your game that don't depend on detailed destruction. That way you won't waste time waiting for levels to load, but the destruction will still occur (so you'll see if something's broken etc as soon as possible).

Objects with many faces (e.g. spheres, cylinders) will perform significantly worse than cubes. (The textures code loops through faces, so more faces leads to more computation.)

I created a manager node [called DestronoiDepthManager rn] which can set the treeHeight of any direct children to a specified node on ready. This means you can quickly change between fast startup time with basic destruction (for debugging, creating scenes, etc) & slower startup time with longer destruction (for testing, release etc) without having to manually change lots of exported variables.

Making a very large mesh into a destronoiNode will not work very well, as you would need a very deep tree in order to see medium to small scale destruction. It's much better to work modularly and add destruction to smaller parts of your scene like boxes, walls, etc.

To be quantitative: if your object has volume $V = L^3$ for an approx length scale $L$, then the binary tree splits this into volumes of $\frac{V}{2^d}$, which have length scales of $\bigg(\frac{V}{2^d}\bigg)^{1/3} = \frac{L}{2^{d/3}}$. So if you want to be able to free the smallest fragments from a destronoiNode, VSTSplittingComponent needs to have an Area3d with radius of approx $\frac{L}{2^{d/3}}$ or bigger. This estimate can also serve as a guide for the depths different sized objects need to be able to produce similar sized fragments. e.g. a 3m object with tree depth 8 will produce similar sized fragments to a 10m object with tree depth 12 (both approx 0.5m in length).

The plugin can deal with many more fragments than what I've suggested above (seems usable at $2^{12} = 4096$ or even more fragments). There are some issues going this high though
- Some warnings (which are by default suppressed) may occur during startup and runtime. They don't seem to negatively impact the plugin (I think it might be something to do with the Bisect() function finding it difficult to split very small meshes)
- Very long startup time (~20+ seconds even for 1 body)
- If you're are using an Area3d to explode objects, by the time the fragments are at say the 8th level, you will have 128 very small fragments that still have 4 levels left. This means that, even for a small explosion, the plugin may be instantiating $~100 \times 2^4 > 1000$ fragments, and godot struggles to do this in less than 16ms i.e. you will drop frames. If you really need this behaviour then perhaps you could come up with a pool system (to avoid needing to instantiate new rigidbodies) and/or if you have a long enough explosion animation, you might be able to spread out instantiating bodies over 10+ frames and you may have a useable system
- very small fragments have a tendency to interact weirdly with characterbody3d's, e.g. launching the player very high

It's important to note that VSTSplitting's runtime is _independent_ of the destronoi node's treeHeight, and only dependent on the `explosionDepth` requested (i.e. the binary tree will only be searched to a level of `explosionDepth`). So essentially the performance considerations are 1) startup time and 2) how many bodies godot can instantiate. (Unless my code is wrong ofc. But from my testing this seems generally true.)

A destronoi node with depth 14 (so $2^{14} = 16,384$ fragments) uses ~400mb of RAM. (but of course, instantiating all these fragments would be difficult in godot, and once they are instantiated then far far far more RAM would be used. The point is that this plugin itself does not use tons of RAM; the number of rigidbodies the plugin creates is the limiting factor, not the way the plugin stores information.)

# Current Disadvantages / Downsides

- Interior / Exterior Texture support only works for meshes with flat exterior surfaces rn
-> actually don't know how to fix... (currently the code uses a shader and matches surfaces by checking normals... maybe there's another way to detect if a face corresponds to an external surface in a shader...)

- CreateConvexShape doesn't create perfect collisionshapes for complex fragments - e.g. if you have some jagged edge of a body that's just been destroyed, the collisionshape may "smooth over" the spiky bits (sometimes quite significantly).
-> perhaps could create a function which creates a more accurate collisionShape, and not use CreateConvexShape?

- Concave shapes are unsupported (but I don't think this matters that much as concave shapes are to be avoided anyway, except in the case of static bodies)

- No physics / engineering finite-element style simulation, like e.g. Red Faction Guerilla has
-> is there a good estimator for weak points in a mesh?

- It's written in C# which not everyone knows
-> make a gdscript port

- Only supports RigidBodies with 1 meshInstance & 1 collisionShape

# Conventions

## Commit Types

- **`feat`**: A new feature
- **`fix`**: A bug fix
- **`docs`**: Documentation-only changes
- **`notes`**: Adding dev-notes / diary entries
- **`style`**: Code style changes (whitespace, formatting, etc. — no code behavior change)
- **`refactor`**: A code change that neither fixes a bug nor adds a feature
- **`perf`**: A code change that improves performance
- **`test`**: Adding or correcting tests
- **`build`**: Changes to the build system or dependencies (e.g. addons)
- **`ci`**: Changes to CI configuration or scripts (e.g. GitHub Actions, Travis)

## Branch Names

Branch names can follow the same names, but are formatted like
- **`feat/adding-x-from-y`**
- **`fix-enemymeshes/removing-extraneous-faces`**

(as **`:`**, **`()`** etc would have to be escaped and branch names cannot have whitespace.)

## Example Messages

```bash
feat(bvh): implement initial bounding volume hierarchy generation
fix(plot): correct axis scaling in star visualisation
docs: update README with usage instructions
build: update python_requirements.txt
```

## Sources

This project follows the [Conventional Commits specification v1.0.0](https://www.conventionalcommits.org/en/v1.0.0/#summary).

The commit types listed above are from the [Angular Commit Message Guidelines](https://github.com/angular/angular/blob/22b96b9/CONTRIBUTING.md#-commit-message-guidelines).
