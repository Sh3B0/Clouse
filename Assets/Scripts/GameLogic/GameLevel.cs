﻿using System.Collections.Generic;
using UnityEngine;

// This scriprs identificates a level entity
// It can put player and cloud in position where they should appear
// It also saves all the required states that should be recovered when player returns to the scene
public class GameLevel : MonoBehaviour {

    // Current level on scene
    public static GameLevel CurrentLevelInstance { get; private set; }

    public int LevelIndex;
    public Player PlayerInstance;
    public Transform PlayerSpawnPoint, CloudSpawnPoint;
    public Transform PlayerExitSpawnPoint, CloudExitSpawnPoint;
    public Animation LoadingFadeAnimation;
    
    public Box BoxPrefab, MetalBoxPrefab;
    
    private void Start() {
        CurrentLevelInstance = this;
        LevelsManager.CurrentLevel = LevelIndex;

        // Restore level state if there was any
        var levelState = CheckpointsManager.LoadLevelState(LevelIndex);
        if (levelState != null) {
            RecreateLevelState(levelState);
        }
        
        // Put player & cloud in their initial places
        // (Unless it is decided by checkpoint manager)
        if (LevelsManager.LoadedByCheckpoint) {
            LevelsManager.LoadedByCheckpoint = false;
            CheckpointsManager.RestorePlayerCloudPos();
        } else {
            if (LevelsManager.EnteredFromLeft) {
                PlayerInstance.MoveToPoint(PlayerSpawnPoint);
                Cloud.me.transform.position = CloudSpawnPoint.position;
            } else {
                PlayerInstance.MoveToPoint(PlayerExitSpawnPoint);
                Cloud.me.transform.position = CloudExitSpawnPoint.position;
            }
        }
        
        // Restore player boxes if there were any
        if (CheckpointsManager.PlayerBoxes != null) {
            foreach (var boxSaved in CheckpointsManager.PlayerBoxes) {
                var box = Instantiate(boxSaved.IsMetal ? MetalBoxPrefab : BoxPrefab);
                var boxTransform = box.transform;
                boxTransform.parent = PlayerInstance.transform;
                boxTransform.localPosition = boxSaved.LocalPos;
                boxTransform.parent = null;
            }
            CheckpointsManager.PlayerBoxes = null;
        }

        // First game launch save
        if (!CheckpointsManager.InitialCheckpointSaved) {
            CheckpointsManager.InitialCheckpointSaved = true;
            SaveLevelState(false);
            CheckpointsManager.CreateCheckpoint();
        }
        
        if (LoadingFadeAnimation) LoadingFadeAnimation.Play(Constants.ANIM_FADE_IN);
    }

    public void SaveLevelState(bool endingOfLevel) {
        if (endingOfLevel && LoadingFadeAnimation) LoadingFadeAnimation.Play(Constants.ANIM_FADE_OUT);
        
        var newSave = new LevelState();

        // If it is not another level, boxes on player should be treated as normal boxes
        if (!endingOfLevel) {
            var initialHeldBox = GameObject.FindWithTag(Constants.TAG_HELD);
            if (initialHeldBox) initialHeldBox.tag = Constants.TAG_BOX;
        }
        
        // Mark all boxes to move to next scene
        var heldBox = GameObject.FindWithTag(Constants.TAG_HELD);
        if (heldBox) {
            // Box that will send raycast
            var raycastOrigin = heldBox.transform.position;
            
            // All Boxes layer
            const int layerMask = 1 << Constants.LAYER_FLOATING | 1 << Constants.LAYER_DEFAULT;
            
            // While boxes on top of origin can be found, mark them as held
            while (Physics.Raycast(raycastOrigin, transform.TransformDirection(Vector3.up), out var hit, Mathf.Infinity, layerMask)) {
                var foundBox = hit.collider.gameObject;
                // No boxes found on top of original
                if (!foundBox.CompareTag(Constants.TAG_BOX)) break;
                foundBox.tag = Constants.TAG_HELD;
                raycastOrigin = foundBox.transform.position;
            }
        
            // Save marked boxes
            var heldBoxes = GameObject.FindGameObjectsWithTag(Constants.TAG_HELD);
            CheckpointsManager.PlayerBoxes = new LevelState.SavedBox[heldBoxes.Length];
            for (var i = 0; i < heldBoxes.Length; i++) {
                heldBoxes[i].transform.parent = PlayerInstance.transform;
                var boxComponent = heldBoxes[i].GetComponent<Box>();
                CheckpointsManager.PlayerBoxes[i] = new LevelState.SavedBox(boxComponent.isMetal, boxComponent.transform.localPosition);
            }
            Player.playerActive = true;
            Player.isMovingBox = false;
        }

        // Save all boxes state
        var boxesOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_BOX);
        newSave.Boxes = new LevelState.SavedBox[boxesOnLevel.Length];
        for (var i = 0; i < boxesOnLevel.Length; i++) {
            var boxComponent = boxesOnLevel[i].GetComponent<Box>();
            newSave.Boxes[i] = new LevelState.SavedBox(boxComponent.isMetal, boxComponent.transform.position);
        }
        
        // Save all generators state by their position
        var generatorsOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_GENERATOR);
        newSave.GeneratorsActivatedByPos = new Dictionary<Vector3, bool>();
        foreach (var generator in generatorsOnLevel) {
            var generatorComponent = generator.GetComponent<Generator>();
            newSave.GeneratorsActivatedByPos.Add(generator.transform.position, generatorComponent.Icon.color == Color.green);
        }
        
        // Save all mirrors state by their position
        var mirrorsOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_MIRROR);
        newSave.MirrorsByPos = new Dictionary<Vector3, LevelState.SavedMirror>();
        foreach (var mirror in mirrorsOnLevel) {
            var mirrorComponent = mirror.GetComponent<Mirror>();
            var mirrorSave = new LevelState.SavedMirror(mirror.transform.rotation, mirrorComponent.mode, mirrorComponent.dir);
            newSave.MirrorsByPos.Add(mirror.transform.position, mirrorSave);
        }

        // Save water state
        var waterOnLevel = FindObjectOfType<WaterRise>();
        if (waterOnLevel) {
            var serializedLayers = waterOnLevel.SerializeWaterLayers();
            newSave.LayersOfWaterObject = serializedLayers;
        }
        
        CheckpointsManager.SaveLevelState(LevelIndex, newSave);
    }
    
    private void RecreateLevelState(LevelState savedState) {
        // Set positions for regular boxes
        var boxesOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_BOX);
        foreach (var box in boxesOnLevel) { Destroy(box); }
        foreach (var box in savedState.Boxes) {
            var newBox = Instantiate(box.IsMetal ? MetalBoxPrefab : BoxPrefab);
            newBox.transform.position = box.LocalPos;
        }
        
        // Set state for generators (That is - activate those that should be activated)
        var generatorsOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_GENERATOR);
        foreach (var generator in generatorsOnLevel) {
            if (savedState.GeneratorsActivatedByPos[generator.transform.position]) {
                generator.GetComponent<Generator>().Work();
            }
        }

        // Set state for mirrors
        var mirrorsOnLevel = GameObject.FindGameObjectsWithTag(Constants.TAG_MIRROR);
        foreach (var mirror in mirrorsOnLevel) {
            var mirrorSave = savedState.MirrorsByPos[mirror.transform.position];
            var mirrorComponent = mirror.GetComponent<Mirror>();
            mirror.transform.rotation = mirrorSave.Rotation;
            mirrorComponent.dir = mirrorSave.Dir;
            mirrorComponent.mode = mirrorSave.Mode;
        }

        // Set state for water
        var waterOnLevel = FindObjectOfType<WaterRise>();
        if (waterOnLevel) {
            waterOnLevel.RecoverWaterFast(savedState.LayersOfWaterObject);
        }
    }

}
