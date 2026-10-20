package main

import (
	"context"
	"fmt"
	"log"
	"strings"
	"time"
)

const (
	clientImage              = "ghcr.io/void-community/portable-minecraft-client:latest"
	voidBaseImage            = "caunt/void:dev"
	sessionImagesProjectName = "void-session-images"
)

// runRefreshLoop keeps session images and the Paper backend current, so new Minecraft, client, Void,
// Paper and plugin releases reach new sessions without restarting the demo.
func (server *Server) runRefreshLoop(ctx context.Context) {
	for {
		server.refreshSessionImages(ctx)
		server.refreshBackend(ctx)

		timer := time.NewTimer(server.nextRefreshDelay())
		select {
		case <-ctx.Done():
			timer.Stop()
			return
		case <-timer.C:
		case <-server.RefreshRequested:
			timer.Stop()
		}
	}
}

func (server *Server) refreshSessionImages(ctx context.Context) {
	clientImageChanged, err := server.pullClientImage(ctx)
	if err != nil {
		log.Printf("Images: failed to refresh %s: %v", clientImage, err)
	}

	baseImageChanged, err := server.refreshVoidBaseImage(ctx)
	if err != nil {
		log.Printf("Images: failed to refresh %s: %v", voidBaseImage, err)
	}

	// A change marks the images unbuilt until a build succeeds, so a failed build is retried on the next pass.
	if clientImageChanged || baseImageChanged {
		server.SessionImagesBuilt = false
	}

	if !server.SessionImagesBuilt {
		output, err := runDockerCommand(ctx, dockerBuildCommandTimeout, "compose", "--project-name", sessionImagesProjectName, "--file", "session.yml", "build", "dashboard", "void", "client")
		if err != nil {
			log.Printf("Images: failed to build session images: %v: %s", err, lastLines(string(output), 20))
			return
		}

		server.SessionImagesBuilt = true
		log.Printf("Images: session images built")
	}

	if output, err := runDockerCommand(ctx, dockerQuickCommandTimeout, "image", "prune", "--force"); err != nil {
		log.Printf("Images: failed to prune dangling images: %v: %s", err, strings.TrimSpace(string(output)))
	}

	server.ImagesReadyOnce.Do(func() {
		close(server.ImagesReady)
		log.Printf("Images: ready for sessions")
	})
}

// pullClientImage pulls the client image the demo's client image builds on, and reports whether it changed.
func (server *Server) pullClientImage(ctx context.Context) (bool, error) {
	previousDigest := server.localImageDigests(ctx, clientImage)

	if output, err := runDockerCommand(ctx, dockerBuildCommandTimeout, "pull", "--quiet", clientImage); err != nil {
		return false, errorWithOutput(err, output)
	}

	currentDigest := server.localImageDigests(ctx, clientImage)
	if currentDigest != previousDigest {
		log.Printf("Images: client image updated to %s", currentDigest)
		return true, nil
	}

	log.Printf("Images: client image up to date (%s)", currentDigest)
	return false, nil
}

// refreshVoidBaseImage pulls caunt/void:dev only when Docker Hub reports a digest the local image lacks.
func (server *Server) refreshVoidBaseImage(ctx context.Context) (bool, error) {
	localDigests := server.localImageDigests(ctx, voidBaseImage)

	remoteDigest, err := server.fetchVoidBaseImageDigest(ctx)
	if err != nil && localDigests != "" {
		return false, err
	}

	if err == nil && strings.Contains(localDigests, remoteDigest) {
		log.Printf("Images: %s up to date (%s)", voidBaseImage, remoteDigest)
		return false, nil
	}

	if output, pullErr := runDockerCommand(ctx, dockerBuildCommandTimeout, "pull", "--quiet", voidBaseImage); pullErr != nil {
		return false, errorWithOutput(pullErr, output)
	}

	log.Printf("Images: %s updated to %s", voidBaseImage, server.localImageDigests(ctx, voidBaseImage))
	return true, nil
}

func (server *Server) localImageDigests(ctx context.Context, image string) string {
	output, err := dockerCommand(ctx, dockerQuickCommandTimeout, "image", "inspect", "--format", "{{join .RepoDigests \",\"}}", image)
	if err != nil {
		return ""
	}

	return strings.TrimSpace(string(output))
}

func (server *Server) waitForImages(ctx context.Context) error {
	select {
	case <-server.ImagesReady:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

func lastLines(text string, count int) string {
	lines := strings.Split(strings.TrimSpace(text), "\n")
	if len(lines) > count {
		lines = lines[len(lines)-count:]
	}

	return strings.Join(lines, "\n")
}

func errorWithOutput(err error, output []byte) error {
	return fmt.Errorf("%w: %s", err, lastLines(string(output), 20))
}
