# Shadow Protocol 🎮

**Comparative Analysis of Finite State Machines and Behavior Trees for Stealth Game AI**

A Unity-based stealth game prototype developed for UWE Bristol Digital Systems Project (UFCFXK-30-3).

---

## 📖 Overview

This project implements enemy AI using two different architectural approaches:
- **Finite State Machine (FSM)** - Traditional state-based approach
- **Behavior Tree (BT)** - Hierarchical, modular approach

Both implementations share identical sensory systems, enabling fair comparison of the decision-making architectures.

---

## 🎯 Features

### AI Behaviors
- **PATROL** - Waypoint-based movement patterns
- **INVESTIGATE** - Response to suspicious sounds
- **SUSPICIOUS** - Partial visual detection state (FSM only)
- **CHASE** - Active pursuit when player fully detected
- **SEARCH** - Methodical examination of last-known position
- **RESPOND_ALERT** - Coordinated response to alerts from other guards

### Sensory Systems
- **VisionSensor** - Field-of-view detection with graduated DetectionMeter
- **HearingSensor** - Spatial audio processing with distance attenuation
- **AIMemory** - Persistent last-known player position storage
- **AlertSystem** - Multi-agent communication and coordination

---

## 📊 Metrics Summary

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

---

## 🗂️ Project Structure

```
Assets/
├── Scripts/
│   ├── AI/
│   │   ├── EnemyAI_Advanced.cs    # FSM Implementation (1,010 lines)
│   │   ├── VisionSensor.cs        # Graduated visual detection
│   │   ├── HearingSensor.cs       # Spatial audio processing
│   │   ├── AIMemory.cs            # Persistent state storage
│   │   └── AlertSystem.cs         # Multi-agent communication
│   ├── BehaviorTree/
│   │   ├── BTCore.cs              # Core framework (529 lines)
│   │   └── EnemyAI_BT.cs          # Game-specific tree (797 lines)
│   ├── Player/
│   │   ├── PlayerControllerTPP.cs
│   │   ├── TPPCameraController.cs
│   │   └── PlayerFlashlight.cs
│   ├── Audio/
│   │   ├── FootstepSystem.cs
│   │   ├── AISoundSystem.cs
│   │   └── StealthAudioManager.cs
│   ├── UI/
│   │   ├── StealthHUD.cs
│   │   └── StealthMinimap.cs
│   └── Metrics/
│       └── AIMetricsCollector.cs
├── Scenes/
└── Prefabs/
```

---

## 🛠️ Requirements

- Unity 2022 LTS or later
- NavMesh components for pathfinding

---

## 🎮 How to Run

1. Clone this repository
2. Open project in Unity 2022 LTS
3. Open the main scene in `Assets/Scenes/`
4. Press Play to test

### Switching AI Architecture
- FSM guards use `EnemyAI_Advanced` component
- BT guards use `EnemyAI_BT` component
- Both use shared `VisionSensor`, `HearingSensor`, and `AlertSystem`

---

## 📚 References

- Bourg & Seemann (2004) - AI for Game Developers
- Isla (2005) - Handling Complexity in the Halo 2 AI
- Colledanchise & Ögren (2018) - Behavior Trees in Robotics and AI
- Millington & Funge (2019) - Artificial Intelligence for Games

---

## 📄 License

This project was developed for academic purposes at UWE Bristol.

---

## 👤 Author

Huy Gia Nguyen - 22044312

University of the West of England, Bristol

UFCFXK-30-3 Digital Systems Project

April 2026
