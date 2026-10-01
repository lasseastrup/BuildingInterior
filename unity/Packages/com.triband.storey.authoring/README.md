# Storey Authoring (editor)

Editor-only package for authoring and baking Storey buildings. Requires `com.triband.storey`.

Because a package's dependencies cannot be git URLs, projects installing from git must add both packages to their manifest (Plan §2). Once the packages are on a scoped registry the dependency resolves on its own.

## Editing layouts

Add **Storey Site** to a GameObject, assign a `.storey` layout and the three materials, and press **Edit layout** in its inspector. The inspector's Shape, Facade and Interior tabs switch the Scene view's Storey tool. The Storey Floors overlay holds the floor count and the floor list. See `docs/EDITOR.md` for what each tool does and how to check it.

