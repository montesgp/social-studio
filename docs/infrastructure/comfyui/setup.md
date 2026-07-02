# Hive - ComfyUI Setup Guide

**Version:** 1.0\
**Audience:** Hive developers

## Purpose

This document describes the standard procedure for preparing a local
ComfyUI environment compatible with Hive image generation workflows.

------------------------------------------------------------------------

# Architecture

``` text
Hive
  │
  ├── Generates Workflow JSON
  │
  ▼
ComfyUI REST API (/prompt)
  │
  ▼
Checkpoint
  │
  ▼
IPAdapter
  │
  ▼
Image Generation
```

Hive does **not** execute ComfyUI workflows stored on disk. It generates
workflow JSON dynamically and submits it through the ComfyUI REST API.

------------------------------------------------------------------------

# Local Development

## Minimum Hardware

### AMD

-   RX580 8 GB (DirectML)
-   16 GB RAM
-   SSD

### NVIDIA

-   RTX 3060 12 GB
-   CUDA 12+
-   32 GB RAM

------------------------------------------------------------------------

# Required Software

-   Git
-   ComfyUI Portable
-   ComfyUI Manager
-   ComfyUI_IPAdapter_plus

------------------------------------------------------------------------

# Directory Layout

``` text
ComfyUI/
├── custom_nodes/
│   ├── ComfyUI-Manager/
│   └── ComfyUI_IPAdapter_plus/
│
└── models/
    ├── checkpoints/
    ├── clip_vision/
    ├── ipadapter/
    ├── loras/
    ├── vae/
    └── controlnet/
```

------------------------------------------------------------------------

# Installation

## 1. Install ComfyUI

Download ComfyUI Portable and verify:

-   http://127.0.0.1:8188

------------------------------------------------------------------------

## 2. Install Custom Nodes

``` bash
cd ComfyUI/custom_nodes

git clone https://github.com/ltdrdata/ComfyUI-Manager.git
git clone https://github.com/cubiq/ComfyUI_IPAdapter_plus.git
```

Restart ComfyUI.

------------------------------------------------------------------------

## 3. Install Models

### Checkpoint

Folder:

``` text
models/checkpoints
```

Recommended:

``` text
realisticVisionV60B1_v51HyperVAE.safetensors
```

------------------------------------------------------------------------

### CLIP Vision

Folder:

``` text
models/clip_vision
```

Required:

``` text
CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors
```

------------------------------------------------------------------------

### IPAdapter

Folder:

``` text
models/ipadapter
```

Required:

``` text
ip-adapter_sd15.safetensors
ip-adapter-plus_sd15.safetensors
ip-adapter-plus-face_sd15.safetensors
ip-adapter-full-face_sd15.safetensors
```

------------------------------------------------------------------------

# Verification Checklist

The following nodes must exist:

-   CheckpointLoaderSimple
-   CLIPTextEncode
-   EmptyLatentImage
-   KSampler
-   VAEDecode
-   SaveImage
-   IPAdapterUnifiedLoader
-   IPAdapterAdvanced

The following API endpoints must respond:

-   GET /object_info
-   GET /system_stats
-   GET /queue
-   GET /history
-   POST /prompt

------------------------------------------------------------------------

# Production Recommendations

Recommended GPUs:

  Tier         GPU
  ------------ -------------------
  Entry        RTX 4070 Ti Super
  Standard     RTX 4090
  Cloud        NVIDIA L40S
  Enterprise   NVIDIA A100

Deploy ComfyUI in Docker and mount models as external volumes.

``` text
/models
/checkpoints
/ipadapter
/clip_vision
/loras
/controlnet
```

------------------------------------------------------------------------

# Automation Roadmap

## hive setup comfy

Responsibilities:

-   Detect GPU
-   Detect CUDA/DirectML
-   Download ComfyUI
-   Install required custom nodes
-   Download required models
-   Generate Hive configuration

------------------------------------------------------------------------

## hive doctor

Validation:

-   ComfyUI reachable
-   Compatible version
-   Required nodes installed
-   Required models installed
-   API responding
-   Workflow compatibility

Example:

``` text
Hive Environment Check

✓ ComfyUI
✓ Checkpoint
✓ IPAdapter
✓ CLIP Vision
✓ API

READY TO GENERATE
```

------------------------------------------------------------------------

# Design Principles

-   Hive should never hardcode model filenames.
-   Hive should discover installed nodes through `/object_info`.
-   Hive should maintain a model registry abstraction.
-   All environment validation should be automated.
