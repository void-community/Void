package voidclient.agent;

import java.util.HashMap;
import java.util.Map;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldNode;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.LabelNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

public final class ClientFrameDiscoveryTest {
    @Test
    public void discoversRenamedLegacyAndDelegatedGraphicsLoops() {
        for (boolean delegated : new boolean[] { false, true }) {
            Map<String, ClassNode> types = new HashMap<String, ClassNode>();
            ClassNode client = type(types, "renamed/Client");
            MethodNode loop = method(client, "outer", "()V");
            LabelNode start = new LabelNode();
            loop.instructions.add(start);
            call(loop, client.name, "frame", "(Z)V");
            loop.instructions.add(new JumpInsnNode(Opcodes.GOTO, start));
            MethodNode frame = method(client, "frame", "(Z)V");
            if (delegated) {
                ClassNode device = type(types, "renamed/Device");
                device.access = Opcodes.ACC_INTERFACE;
                method(device, "present", "()V").access = Opcodes.ACC_ABSTRACT;
                ClassNode graphics = type(types, "renamed/Graphics");
                graphics.interfaces.add(device.name);
                MethodNode present = method(graphics, "present", "()V");
                call(present, "org/lwjgl/glfw/GLFW", "glfwSwapBuffers", "(J)V");
                call(frame, device.name, "present", "()V");
                call(frame, "org/lwjgl/glfw/GLFW", "glfwWindowShouldClose", "(J)Z");
            } else {
                call(frame, "org/lwjgl/opengl/Display", "update", "()V");
                call(frame, "org/lwjgl/opengl/Display", "isCloseRequested", "()Z");
            }
            FramePlan plan = ClientFrameDiscovery.discover(types, client.name);
            Assert.assertEquals("outer", plan.loopName);
            Assert.assertEquals("frame", plan.frameName);
        }
    }

    @Test
    public void discoversSdlGraphicsLoops() {
        for (String[] presentation : new String[][] {
            { "org/lwjgl/sdl/SDLVideo", "SDL_GL_SwapWindow", "(J)Z" },
            { "org/lwjgl/vulkan/KHRSwapchain", "vkQueuePresentKHR", "(Lorg/lwjgl/vulkan/VkQueue;Lorg/lwjgl/vulkan/VkPresentInfoKHR;)I" } }) {
            Map<String, ClassNode> types = new HashMap<String, ClassNode>();
            ClassNode client = type(types, "renamed/Client");
            MethodNode loop = method(client, "outer", "()V");
            LabelNode start = new LabelNode();
            loop.instructions.add(start);
            ClassNode events = type(types, "renamed/Events");
            call(method(events, "poll", "()V"), "org/lwjgl/sdl/SDLEvents", "SDL_PollEvent", "(Lorg/lwjgl/sdl/SDL_Event;)Z");
            call(loop, events.name, "poll", "()V");
            call(loop, client.name, "frame", "(Z)V");
            call(loop, client.name, "recover", "()V");
            loop.instructions.add(new JumpInsnNode(Opcodes.GOTO, start));
            MethodNode frame = method(client, "frame", "(Z)V");
            call(frame, presentation[0], presentation[1], presentation[2]);
            // Recovery paths also pump events and present, only further from the presentation call.
            MethodNode recover = method(client, "recover", "()V");
            call(recover, events.name, "poll", "()V");
            call(recover, client.name, "frame", "(Z)V");
            FramePlan plan = ClientFrameDiscovery.discover(types, client.name);
            Assert.assertEquals("outer", plan.loopName);
            Assert.assertEquals("frame", plan.frameName);
        }
    }

    @Test
    public void findsLoopWhenScreenOwnershipIsComposed() {
        Map<String, ClassNode> types = loop();
        ClassNode screens = type(types, "renamed/Screens");
        types.get("renamed/Client").fields.add(new FieldNode(0, "screens", "Lrenamed/Screens;", null, null));
        Assert.assertEquals("renamed/Client", ClientFrameDiscovery.discover(types, screens.name).owner);
    }

    @Test
    public void rejectsAmbiguousOuterLoops() {
        Map<String, ClassNode> types = loop();
        ClassNode client = types.get("renamed/Client");
        MethodNode second = method(client, "otherLoop", "()V");
        LabelNode start = new LabelNode();
        second.instructions.add(start);
        call(second, client.name, "frame", "()V");
        second.instructions.add(new JumpInsnNode(Opcodes.GOTO, start));
        try {
            ClientFrameDiscovery.discover(types, client.name);
            Assert.fail("Ambiguous loops must not be guessed");
        } catch (IllegalStateException exception) {
            Assert.assertTrue(exception.getMessage().contains("otherLoop"));
        }
    }

    private static Map<String, ClassNode> loop() {
        Map<String, ClassNode> types = new HashMap<String, ClassNode>();
        ClassNode client = type(types, "renamed/Client");
        MethodNode loop = method(client, "outer", "()V");
        LabelNode start = new LabelNode();
        loop.instructions.add(start);
        call(loop, client.name, "frame", "()V");
        loop.instructions.add(new JumpInsnNode(Opcodes.GOTO, start));
        MethodNode frame = method(client, "frame", "()V");
        call(frame, "org/lwjgl/opengl/Display", "update", "()V");
        call(frame, "org/lwjgl/opengl/Display", "isCloseRequested", "()Z");
        return types;
    }

    private static ClassNode type(Map<String, ClassNode> types, String name) {
        ClassNode type = new ClassNode();
        type.name = name;
        type.superName = "java/lang/Object";
        types.put(name, type);
        return type;
    }

    private static MethodNode method(ClassNode type, String name, String descriptor) {
        MethodNode method = new MethodNode(Opcodes.ACC_PUBLIC, name, descriptor, null, null);
        type.methods.add(method);
        return method;
    }

    private static void call(MethodNode method, String owner, String name, String descriptor) {
        method.instructions.add(new MethodInsnNode(Opcodes.INVOKEVIRTUAL, owner, name, descriptor, false));
    }
}
