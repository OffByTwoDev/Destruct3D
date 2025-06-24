# Multi Scale Procedural Destruction in Godot

Note: Jolt is highly recommended when using this plugin. Using the default engine leads to fragments oscillating unphysically (might be something to do with Centre of Mass calculations).

# Credits

I am extremely thankful to seadaemon for the [Destronoi plugin](https://github.com/seadaemon/Destronoi), which this plugin was built off of. If Destronoi didn't exist this project would've taken significantly longer and I may never have made it.

For clarity: my C# code "VSTNode.cs" and "DestronoiNode.cs" were initially a direct port of seadaemon's gdscript code. I then added some functions and different components to each to create this project.

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

(as ":" would have to be escaped and branch names cannot have whitespace).

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
