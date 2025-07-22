# Multi Scale Procedural Destruction in Godot

Note: Jolt is highly recommended when using this plugin. Using the default engine leads to fragments oscillating unphysically (might be something to do with Centre of Mass calculations).

# Credits

I am extremely thankful to seadaemon for the [Destronoi plugin](https://github.com/seadaemon/Destronoi), which this plugin was built off of. If Destronoi didn't exist this project would've taken significantly longer and I may never have made it.

For clarity: my C# code "VSTNode.cs" and "DestronoiNode.cs" were initially a direct port of seadaemon's gdscript code. I then added some functions and different components to each to create this project.

# Recommendations & Performance

For comparison my system is:
- AMD Ryzen 5 5600 6-Core Processor (3.5 GHz)
- AMD Radeon 6600XT (8GB VRAM)
- 32GB RAM

I would recommend using a binary tree depth of 8 (so 2^8 = 256 fragments) as a starting point. This seems to give good looking destruction and has reasonable startup time (~ 5 seconds for me) for ~10 objects.

I would recommend setting treeHeight to e.g. 3 whilst you are making parts of your game that don't depend on detailed destruction. That way you won't waste time waiting for levels to load, but the destruction will still occur (so you'll see if something's broken etc as soon as possible).

I would consider making a manager to set the appropriate debug vs testing vs release treeHeight. e.g. you could add some code to DestronoiNode.cs to get the current game state (debug, full destruction, etc) on ready and set the treeHeight appropriately. Or simply just remove the `[Export]` tag on the treeHeight variable and treat it as a constant - that way you only have 1 line of code to change.

The plugin can deal with many more fragments than what I've suggested above (seems usable at 2^12 = 4096 or even more fragments). There are some issues going this high though
- Some warnings (which are by default suppressed) may occur during startup and runtime. They don't seem to negatively impact the plugin (I think it might be something to do with the Bisect() function finding it difficult to split very small meshes)
- Very long startup time (~15+ seconds even for 1 body)
- If (like me) you are using an area3d to explode objects, by the time the fragments are at say the 8th level, you will have 128 very small fragments that still have 4 levels left. This means that, even for a small explosion, the plugin may be instantiating ~ 100 * 2^4 > 1000 fragments, and godot / jolt cannot do this in less than 16ms i.e. you will drop frames. If you really need this behaviour then perhaps you could come up with a pool system (to avoid needing to instantiate new rigidbodies) and/or if you have a long enough animation, you might be able to spread out instantiating bodies over 10+ frames and you may have a useable system
- very small fragments have a tendency to interact weirdly with characterbody3d's, e.g. launching the player very high

It's important to note that VSTSplitting's runtime is _independent_ of the treeHeight, and only dependent on the explosionDepth requested (i.e. the Binary tree will only be searched to level = explosionDepth). So essentially the performance considerations are 1) startup time and 2) how many bodies godot can instantiate. (Unless my code is wrong ofc. But from my testing this seems generally true.)

# Commit Convention

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
