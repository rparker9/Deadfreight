# Refactor: names and folders

## Renamed (GUIDs kept, so components stay attached)
| Old | New | Folder |
|---|---|---|
| AppRoot, AppFlow, NetSession | (same) | Core/ |
| HudController.cs (file) | HUDController.cs (matches class) | UI/ |
| PlayerAnimationClient | PlayerAnimator | Player/Presentation/ |
| PlayerIKClient | PlayerHeadLook | Player/Presentation/ |
| PlayerGrabController | PlayerInteractor | Interaction/ |
| NetworkGrabbable | Pickup (now an Interactable) | Interaction/ |
| InteractableUnityEvent | InteractableEvent | Interaction/ |
| Wagon | Vehicle | Vehicles/ |
| WagonWheel | VehicleWheel | Vehicles/ |
| VehicleSeats | VehicleSeating | Vehicles/ |
| PlayerSeatController | PlayerSeating | Vehicles/ |
| VehicleSeat | (same) | Vehicles/ |
| WagonSurface | GroundSurface | World/ |
| PlayerState.GrabbedObject | PlayerState.HeldObject | |

## Removed
- GrabTether (rope line) and all dragging (drag mode, lift force, drag settings)
- HoldPose is now 0 = hands free, 1 = carrying

## Rule
Feature folders (Interaction, Vehicles) may use Player/. Player/ never uses a feature,
except PlayerInputController, which routes input (seated -> PlayerSeating, else -> PlayerInteractor).
