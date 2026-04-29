🚀 Overview
Bug-Off is an immersive Augmented Reality (AR) application designed to bridge the gap between digital catalogs and physical spaces. Built for the Triverse Hackathon, this app allows users to visualize, scale, and arrange furniture in real-time with high precision, helping users make informed interior design decisions without lifting a heavy box.

✨ Key Features
Dynamic Furniture Catalog: Seamlessly switch between different furniture types (Sofa, Chair, Round Table) via a thumb-friendly bottom-tray UI.

Intelligent Placement: One-instance-per-type logic ensures a clean user experience—tapping the floor moves the existing model instead of cluttering the scene with duplicates.

Precision Transforms:

Drag to Move: Smoothly slide objects across detected planes.

Pinch to Scale: Resize furniture to see how it fits in small vs. large rooms.

Twist to Rotate: 360-degree rotation control for perfect alignment.

Auto-Calibration: Custom scripts to handle model import offsets, ensuring every piece of furniture spawns upright and at a realistic scale.

🛠️ Tech Stack
Game Engine: Unity 2022.3 (LTS)

AR Framework: AR Foundation (leveraging ARCore for Android)

Language: C#

UI: Unity URP (Universal Render Pipeline) compatible UI

📦 How to Run
Clone the Repo: git clone [Your Repo Link]

Open in Unity: Use Unity Hub to open the project folder.

Build Settings: Ensure the platform is set to Android or iOS.

Deploy: Build the APK and install it on an ARCore/ARKit compatible device.
