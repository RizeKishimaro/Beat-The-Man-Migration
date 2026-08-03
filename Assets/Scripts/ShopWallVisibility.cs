using UnityEngine;

/// <summary>
/// Hides the shop's exterior walls while the player is inside the building,
/// so the isometric camera can see the interior. Attach to the interior
/// trigger volume (needs a Collider defining the interior bounds) and assign
/// the wall renderer(s) to hide.
///
/// Checks the player's position against the interior bounds every frame
/// rather than relying on Unity's OnTrigger messages, since CharacterController
/// vs. static trigger colliders can be unreliable depending on Rigidbody setup.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ShopWallVisibility : MonoBehaviour
{
    [Tooltip("Renderers to hide while the player is inside (e.g. the ShopWalls mesh renderer).")]
    public Renderer[] wallRenderers;

    [Tooltip("Optional explicit interior volume. If left empty, uses this GameObject's own Collider.")]
    public Collider interiorVolume;

    private Transform player;
    private bool wallsHidden;

    private void Start()
    {
        if (interiorVolume == null)
            interiorVolume = GetComponent<Collider>();

        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null)
            player = pc.transform;
    }

    private void Update()
    {
        if (player == null || interiorVolume == null)
            return;

        bool inside = interiorVolume.bounds.Contains(player.position);
        if (inside != wallsHidden)
        {
            wallsHidden = inside;
            SetWallsVisible(!inside);
        }
    }

    private void SetWallsVisible(bool visible)
    {
        if (wallRenderers == null)
            return;

        for (int i = 0; i < wallRenderers.Length; i++)
        {
            if (wallRenderers[i] != null)
                wallRenderers[i].enabled = visible;
        }
    }
}
