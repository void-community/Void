package voidclient.agent;

import com.mojang.datafixers.DataFixerBuilder;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URL;
import java.net.URLClassLoader;
import java.util.Arrays;
import java.util.Collection;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.junit.runners.Parameterized;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

@RunWith(Parameterized.class)
public class DataFixerWarmupTransformerTest {
    private final URL library;

    public DataFixerWarmupTransformerTest(String version, URL library) {
        this.library = library;
    }

    @Parameterized.Parameters(name = "{0}")
    public static Collection<Object[]> libraries() {
        return Arrays.asList(new Object[][] {
            {"4.1.27", DataFixerWarmupTransformerTest.class.getResource("/legacy-datafixerupper.jar")},
            {"5.0.28", DataFixerBuilder.class.getProtectionDomain().getCodeSource().getLocation()}
        });
    }

    @Test
    public void baselineEagerlyWarmsRulesAndConvertsData() throws Exception {
        String[] result = run(false).split(":");
        assertTrue(Integer.parseInt(result[0]) > 0);
        assertEquals("old-updated", result[1]);
    }

    @Test
    public void deferredWarmupStillConvertsOldData() throws Exception {
        assertEquals("0:old-updated", run(true));
    }

    private String run(boolean transform) throws Exception {
        try (URLClassLoader loader = new URLClassLoader(new URL[] {library}, getClass().getClassLoader()) {
            @Override
            protected Class<?> loadClass(String name, boolean resolve) throws ClassNotFoundException {
                boolean dataFixer = name.startsWith("com.mojang.");
                if (!dataFixer && !name.startsWith(DataFixerConversion.class.getName()))
                    return super.loadClass(name, resolve);

                synchronized (getClassLoadingLock(name)) {
                    Class<?> loaded = findLoadedClass(name);
                    if (loaded == null) {
                        String resourceName = name.replace('.', '/') + ".class";
                        URL resource = dataFixer ? findResource(resourceName) : getParent().getResource(resourceName);
                        if (resource == null)
                            throw new ClassNotFoundException(name);
                        try (InputStream input = resource.openStream()) {
                            ByteArrayOutputStream output = new ByteArrayOutputStream();
                            byte[] buffer = new byte[8192];
                            int length;
                            while ((length = input.read(buffer)) != -1)
                                output.write(buffer, 0, length);
                            byte[] bytes = output.toByteArray();
                            if (transform) {
                                byte[] replacement = new DataFixerWarmupTransformer().transform(this, name.replace('.', '/'), null, null, bytes);
                                if (replacement != null)
                                    bytes = replacement;
                            }
                            loaded = defineClass(name, bytes, 0, bytes.length);
                        } catch (IOException exception) {
                            throw new ClassNotFoundException(name, exception);
                        }
                    }
                    if (resolve)
                        resolveClass(loaded);
                    return loaded;
                }
            }
        }) {
            return (String) loader.loadClass(DataFixerConversion.class.getName()).getMethod("run").invoke(null);
        }
    }
}
