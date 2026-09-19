package voidclient.agent;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;

final class ClientFrameDiscovery {
    private ClientFrameDiscovery() {
    }

    static FramePlan discover(Map<String, ClassNode> types, String screenOwner) {
        List<String> owners = new ArrayList<String>();
        owners.add(screenOwner);
        for (ClassNode type : types.values())
            for (org.objectweb.asm.tree.FieldNode field : type.fields)
                if ((field.access & Opcodes.ACC_STATIC) == 0 && field.desc.equals("L" + screenOwner + ";") && !owners.contains(type.name))
                    owners.add(type.name);
        List<FramePlan> frames = discoverFrames(types, owners);
        if (frames.size() != 1)
            throw new IllegalStateException("Expected one outer client frame boundary for " + screenOwner + " but found " + frames);
        return frames.get(0);
    }

    private static List<FramePlan> discoverFrames(Map<String, ClassNode> types, List<String> owners) {
        Map<String, Set<String>> callers = new HashMap<String, Set<String>>();
        Set<String> presentation = new HashSet<String>();
        Set<String> windowClose = new HashSet<String>();
        Set<String> eventPump = new HashSet<String>();
        Set<String> calledClientMethods = new HashSet<String>();
        for (ClassNode type : types.values()) {
            Set<String> parents = parents(types, type, new HashSet<String>());
            for (String parent : parents) {
                ClassNode ancestor = types.get(parent);
                if (ancestor == null)
                    continue;
                for (MethodNode inherited : ancestor.methods)
                    if ((inherited.access & (Opcodes.ACC_STATIC | Opcodes.ACC_PRIVATE)) == 0 && !inherited.name.startsWith("<")) {
                        String implementation = key(type.name, inherited.name, inherited.desc);
                        String declaration = key(parent, inherited.name, inherited.desc);
                        edge(callers, implementation, declaration);
                        edge(callers, declaration, implementation);
                    }
            }
            for (MethodNode method : type.methods) {
                String caller = key(type.name, method.name, method.desc);
                for (AbstractInsnNode instruction : method.instructions) {
                    if (!(instruction instanceof MethodInsnNode))
                        continue;
                    MethodInsnNode call = (MethodInsnNode) instruction;
                    if (call.name.startsWith("<"))
                        continue;
                    String callee = key(call.owner, call.name, call.desc);
                    edge(callers, callee, caller);
                    if (type.name.equals(call.owner))
                        calledClientMethods.add(key(call.owner, call.name, call.desc));
                    // Stable graphics-library contracts; no Minecraft symbols or version tables.
                    if ("org/lwjgl/glfw/GLFW".equals(call.owner) && "glfwSwapBuffers".equals(call.name)
                        || "org/lwjgl/sdl/SDLVideo".equals(call.owner) && "SDL_GL_SwapWindow".equals(call.name)
                        || "org/lwjgl/vulkan/KHRSwapchain".equals(call.owner) && "vkQueuePresentKHR".equals(call.name)
                        || "org/lwjgl/opengl/Display".equals(call.owner) && "update".equals(call.name))
                        presentation.add(callee);
                    if ("org/lwjgl/glfw/GLFW".equals(call.owner) && "glfwWindowShouldClose".equals(call.name)
                        || "org/lwjgl/opengl/Display".equals(call.owner) && "isCloseRequested".equals(call.name))
                        windowClose.add(callee);
                    if ("org/lwjgl/sdl/SDLEvents".equals(call.owner) && "SDL_PollEvent".equals(call.name))
                        eventPump.add(callee);
                }
            }
        }

        Map<String, Integer> presents = distances(callers, presentation);
        Map<String, Integer> closes = distances(callers, windowClose);
        Map<String, Integer> pumps = distances(callers, eventPump);
        // SDL has no close query: its loop body pumps the event queue, so the frame is the call nearest to presentation.
        boolean closeQueried = !windowClose.isEmpty();
        List<FramePlan> candidates = new ArrayList<FramePlan>();
        int shortest = Integer.MAX_VALUE;
        for (String clientName : owners)
        for (MethodNode method : types.get(clientName).methods) {
            if ((method.access & Opcodes.ACC_STATIC) != 0 || method.name.startsWith("<")
                || !"()V".equals(method.desc) || calledClientMethods.contains(key(clientName, method.name, method.desc)))
                continue;
            Set<String> found = new HashSet<String>();
            for (AbstractInsnNode instruction : method.instructions) {
                if (!(instruction instanceof JumpInsnNode))
                    continue;
                JumpInsnNode jump = (JumpInsnNode) instruction;
                int start = method.instructions.indexOf(jump.label);
                int end = method.instructions.indexOf(jump);
                if (!closeQueried && !reaches(method, start, end, pumps))
                    continue;
                for (int position = start; position < end; position++) {
                    AbstractInsnNode nested = method.instructions.get(position);
                    if (!(nested instanceof MethodInsnNode))
                        continue;
                    MethodInsnNode call = (MethodInsnNode) nested;
                    String callee = key(call.owner, call.name, call.desc);
                    if (!clientName.equals(call.owner) || call.getOpcode() == Opcodes.INVOKESTATIC
                        || !presents.containsKey(callee) || closeQueried && !closes.containsKey(callee)
                        || !found.add(callee))
                        continue;
                    int distance = (closeQueried ? closes : presents).get(callee).intValue();
                    if (distance < shortest) {
                        candidates.clear();
                        shortest = distance;
                    }
                    if (distance == shortest)
                        candidates.add(new FramePlan(clientName, method.name, method.desc, call.name, call.desc));
                }
            }
        }
        return candidates;
    }

    private static boolean reaches(MethodNode method, int start, int end, Map<String, Integer> distances) {
        for (int position = start; position < end; position++) {
            AbstractInsnNode instruction = method.instructions.get(position);
            if (!(instruction instanceof MethodInsnNode))
                continue;
            MethodInsnNode call = (MethodInsnNode) instruction;
            if (distances.containsKey(key(call.owner, call.name, call.desc)))
                return true;
        }
        return false;
    }

    private static Set<String> parents(Map<String, ClassNode> types, ClassNode type, Set<String> result) {
        List<String> names = new ArrayList<String>(type.interfaces);
        if (type.superName != null)
            names.add(type.superName);
        for (String name : names)
            if (result.add(name) && types.containsKey(name))
                parents(types, types.get(name), result);
        return result;
    }

    private static String key(String owner, String name, String descriptor) {
        return owner + '.' + name + descriptor;
    }

    private static void edge(Map<String, Set<String>> callers, String callee, String caller) {
        Set<String> methods = callers.get(callee);
        if (methods == null) {
            methods = new HashSet<String>();
            callers.put(callee, methods);
        }
        methods.add(caller);
    }

    private static Map<String, Integer> distances(Map<String, Set<String>> callers, Set<String> roots) {
        Map<String, Integer> result = new HashMap<String, Integer>();
        ArrayDeque<String> remaining = new ArrayDeque<String>();
        for (String root : roots) {
            result.put(root, Integer.valueOf(0));
            remaining.add(root);
        }
        while (!remaining.isEmpty()) {
            String method = remaining.remove();
            Set<String> parents = callers.get(method);
            if (parents == null)
                continue;
            for (String parent : parents)
                if (!result.containsKey(parent)) {
                    result.put(parent, Integer.valueOf(result.get(method).intValue() + 1));
                    remaining.add(parent);
                }
        }
        return result;
    }
}

final class FramePlan {
    final String owner;
    final String loopName;
    final String loopDescriptor;
    final String frameName;
    final String frameDescriptor;

    FramePlan(String owner, String loopName, String loopDescriptor, String frameName, String frameDescriptor) {
        this.owner = owner;
        this.loopName = loopName;
        this.loopDescriptor = loopDescriptor;
        this.frameName = frameName;
        this.frameDescriptor = frameDescriptor;
    }

    @Override
    public String toString() {
        return owner + '.' + loopName + loopDescriptor + " -> " + frameName + frameDescriptor;
    }
}
