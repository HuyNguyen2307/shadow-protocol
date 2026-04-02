# Shadow Protocol 🎮

**Comparative Analysis of Finite State Machines and Behavior Trees for Stealth Game AI**

A Unity-based stealth game prototype developed for UWE Bristol Digital Systems Project (UFCFXK-30-3).

---

## 📖 Overview

This project implements enemy AI using two different architectural approaches:
- **Finite State Machine (FSM)** - Traditional state-based approach (`EnemyAI_Advanced.cs`)
- **Behavior Tree (BT)** - Hierarchical, modular approach (`EnemyAI_BT.cs`)

Both implementations share identical sensory systems, enabling fair comparison of the decision-making architectures.

---

## 🎯 Features

### AI Behaviors (6 States/5 Branches)
| Behavior | Description |
|----------|-------------|
| **PATROL** | Waypoint-based movement patterns |
| **INVESTIGATE** | Response to suspicious sounds |
| **SUSPICIOUS** | Partial visual detection (FSM) |
| **CHASE** | Active pursuit when player fully detected |
| **SEARCH** | Methodical search at last-known position |
| **RESPOND_ALERT** | Coordinated response to ally alerts |

### Sensory Systems
- **VisionSensor** - Field-of-view detection with graduated DetectionMeter
- **HearingSensor** - Spatial audio processing with distance attenuation
- **AIMemory** - Persistent last-known player position storage
- **AlertSystem** - Multi-agent communication and coordination

---

## 📊 Code Metrics

| Metric | FSM | Behavior Tree |
|--------|-----|---------------|
| Lines of Code | 1,010 | 1,326 (797+529) |
| States/Branches | 6 | 5 |
| Total Nodes | 6 | 19 |

### Runtime Data (87.2s session, 8 agents)
| State | Transitions | Percentage |
|-------|-------------|------------|
| PATROL | 28 | 40% |
| INVESTIGATE | 29 | 41% |
| SUSPICIOUS | 8 | 11% |
| CHASE | 3 | 4% |
| SEARCH | 2 | 3% |
| RESPOND_ALERT | 0 | 0% |
| **Total** | **70** | **100%** |

---

## 🗂️ Project Structure

```
Scripts/
├── AI/
│   ├── AIMemory.cs              # Persistent state storage
│   ├── AlertSystem.cs           # Multi-agent communication
│   ├── EnemyAI_Advanced.cs      # FSM Implementation (1,010 lines)
│   ├── HearingSensor.cs         # Spatial audio processing
│   └── PlayerNoiseSystem.cs     # Player sound generation
│
├── BehaviorTree/
│   ├── BTCore.cs                # Core BT framework (529 lines)
│   └── EnemyAI_BT.cs            # Game-specific tree (797 lines)
│
├── EnemyAI.cs                   # Basic AI script
├── EnemyPatrol.cs               # Simple patrol behavior
├── EnemyCatchZone.cs            # Player catch detection
├── VisionSensor.cs              # Visual detection system
│
├── GameManager.cs               # Game state management
├── GoalTrigger.cs               # Level completion
│
├── PlayerController.cs          # Basic player control
├── PlayerControllerTPP.cs       # Third-person controller
├── PlayerFlashlight.cs          # Flashlight mechanics
├── TPPCameraController.cs       # Third-person camera
├── TopDownCamera.cs             # Alternative camera
│
├── FootstepSystem.cs            # Player footstep sounds
├── AISoundSystem.cs             # AI audio feedback
├── StealthAudioManager.cs       # Audio management
│
├── StealthHUD.cs                # UI display
├── StealthMinimap.cs            # Minimap system
│
├── AIMetricsCollector.cs        # Runtime data collection
└── MetricsLogger.cs             # Metrics export
```

---

## 🛠️ Requirements

- Unity 2022 LTS or later
- NavMesh components for AI pathfinding

---

## 🎮 How to Run

1. Clone or download this repository
2. Open project in Unity 2022 LTS
3. Open the main scene
4. Press Play to test

### AI Architecture Selection
- **FSM Guards**: Use `EnemyAI_Advanced` component
- **BT Guards**: Use `EnemyAI_BT` component
- Both use shared sensors: `VisionSensor`, `HearingSensor`, `AlertSystem`

---

## 🔬 Key Findings

| Criterion | FSM | Behavior Tree | Winner |
|-----------|-----|---------------|--------|
| Ease of Understanding | ★★★★★ | ★★★☆☆ | FSM |
| Extensibility | ★★☆☆☆ | ★★★★★ | BT |
| Maintainability | ★★★☆☆ | ★★★★☆ | BT |
| Debugging | ★★★★☆ | ★★★☆☆ | FSM |
| Reusability | ★★☆☆☆ | ★★★★★ | BT |

**Conclusion**: FSM is better for stable requirements; BT is better for iterative development.

---



## 📄 License

This project was developed for academic purposes at UWE Bristol.

---

## 👤 Author

Huy Nguyen - 22044312

University of the West of England, Bristol  
UFCFXK-30-3 Digital Systems Project  
April 2026
